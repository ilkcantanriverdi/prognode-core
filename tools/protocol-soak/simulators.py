"""PROGNODE protocol soak: simulated PLCs and brokers for the four protocols Core reads.

Each simulator serves the same value model so the soak harness can check every value it reads:
  * counter k  = (whole Unix seconds + k) % 30000, updated continuously (Core must keep up);
  * fixed values per data type: Word 1234, Int16 -1234, Int32 -123456, UInt32 4000000000,
    Float32 12.5 (Modbus also in CDAB word order), Bool true.

Usage (the harness starts and stops these itself; each runs until killed):
  python simulators.py modbus  --port 15020 --counters 200
  python simulators.py s7      --port 11102 --counters 200
  python simulators.py opcua   --port 14841 --counters 200 --pki <folder>
  python simulators.py mqtt    --port 11883 --counters 50      (publisher; the broker runs in the harness)
  --user / --password: OPC UA login and MQTT broker credentials (the soak always passes them)
"""
import argparse
import asyncio
import ctypes
import struct
import sys
import threading
import time

MOD = 30000


def counter(k: int) -> int:
    return (int(time.time()) + k) % MOD


def words_float(value: float, order: str = "ABCD") -> list[int]:
    b = struct.pack(">f", value)
    hi, lo = (b[0] << 8) | b[1], (b[2] << 8) | b[3]
    return [hi, lo] if order == "ABCD" else [lo, hi]


def words_u32(value: int) -> list[int]:
    return [(value >> 16) & 0xFFFF, value & 0xFFFF]


def words64(big_endian: bytes) -> list[int]:
    return [(big_endian[i] << 8) | big_endian[i + 1] for i in range(0, 8, 2)]


# ----------------------------------------------------------------------------- Modbus TCP
def run_modbus(port: int, counters: int) -> None:
    from pymodbus.datastore import ModbusDeviceContext, ModbusSequentialDataBlock, ModbusServerContext
    from pymodbus.server import StartAsyncTcpServer

    size = 1100
    # pymodbus data blocks are 1-based internally: protocol address a is stored at block index a+1.
    # Holding registers reach 19011 for the six-digit / 64-bit Tags (419000..).
    hr = ModbusSequentialDataBlock(0, [0] * (19100 + 1))
    ir = ModbusSequentialDataBlock(0, [0] * (size + 1))
    co = ModbusSequentialDataBlock(0, [False] * (size + 1))
    di = ModbusSequentialDataBlock(0, [False] * (size + 1))
    device = ModbusDeviceContext(hr=hr, ir=ir, co=co, di=di)
    context = ModbusServerContext(devices=device, single=True)

    fixed = {1000: [1234], 1001: [(-1234) & 0xFFFF], 1002: words_u32((-123456) & 0xFFFFFFFF),
             1004: words_u32(4000000000), 1006: words_float(12.5, "ABCD"), 1008: words_float(12.5, "CDAB")}

    def write(block, address, values):
        block.setValues(address + 1, values)

    for address, values in fixed.items():
        write(hr, address, values)
    # 64-bit values at six-digit addresses 419000 (Float64), 419004 (UInt64), 419008 (Int64, CDAB).
    write(hr, 18999, words64(struct.pack(">d", 12.5)))
    write(hr, 19003, words64(struct.pack(">Q", 5000000000)))
    write(hr, 19007, list(reversed(words64(struct.pack(">q", -123456789012)))))
    write(co, 0, [True])
    write(di, 0, [True])

    async def update():
        while True:
            write(hr, 0, [counter(k) for k in range(counters)])
            write(ir, 0, [counter(k) for k in range(min(counters, 100))])
            await asyncio.sleep(0.2)

    async def main():
        asyncio.get_running_loop().create_task(update())
        await StartAsyncTcpServer(context=context, address=("127.0.0.1", port))

    asyncio.run(main())


# ----------------------------------------------------------------------------- Modbus RTU
def run_modbus_rtu(serial_port: str, counters: int, baud: int = 9600, parity: str = "E") -> None:
    r"""pymodbus RTU slaves on a serial port (e.g. one end of a com0com pair, \\.\CNCB0).
    Unit 1 serves the Modbus TCP simulator's map; unit 2 the same map with counters offset by 1000,
    so a reader that mixes up units on a shared line reads wrong values."""
    from pymodbus import FramerType
    from pymodbus.datastore import ModbusDeviceContext, ModbusSequentialDataBlock, ModbusServerContext
    from pymodbus.server import StartAsyncSerialServer

    def unit(shift: int):
        hr = ModbusSequentialDataBlock(0, [0] * (19100 + 1))
        ir = ModbusSequentialDataBlock(0, [0] * 1101)
        co = ModbusSequentialDataBlock(0, [False] * 1101)
        di = ModbusSequentialDataBlock(0, [False] * 1101)
        hr.setValues(1001, [1234])
        hr.setValues(1007, words_float(12.5, "ABCD"))
        hr.setValues(19000, words64(struct.pack(">d", 12.5)))
        co.setValues(1, [True])
        return hr, ir, co, di, shift, ModbusDeviceContext(hr=hr, ir=ir, co=co, di=di)

    units = {1: unit(0), 2: unit(1000)}
    context = ModbusServerContext(devices={k: v[5] for k, v in units.items()}, single=False)

    async def update():
        while True:
            for hr, ir, _, _, shift, _ in units.values():
                hr.setValues(1, [counter(k + shift) for k in range(counters)])
                ir.setValues(1, [counter(k + shift) for k in range(min(counters, 100))])
            await asyncio.sleep(0.2)

    async def main():
        asyncio.get_running_loop().create_task(update())
        await StartAsyncSerialServer(context=context, framer=FramerType.RTU, port=serial_port,
                                     baudrate=baud, bytesize=8, parity=parity, stopbits=1)

    asyncio.run(main())


# ----------------------------------------------------------------------------- Mitsubishi MC
MC_CODES = {0x9C: "X", 0x9D: "Y", 0x90: "M", 0x91: "SM", 0xA8: "D", 0xB4: "W", 0xAF: "R", 0xC2: "TN"}
MC_BIT_CODES = {0x9C, 0x9D, 0x90, 0x91}


def mc_memory() -> dict:
    """Device memory of the MC simulator. 32/64-bit values are stored low word first (Mitsubishi)."""
    mem = {code: {} for code in MC_CODES}

    def put_words(code, start, words):
        for i, w in enumerate(words):
            mem[code][start + i] = w & 0xFFFF

    def low_first(fmt, value):
        raw = struct.pack("<" + fmt, value)
        return [raw[i] | raw[i + 1] << 8 for i in range(0, len(raw), 2)]

    put_words(0xA8, 1000, [1234])
    put_words(0xA8, 1001, low_first("h", -1234))
    put_words(0xA8, 1002, low_first("i", -123456))
    put_words(0xA8, 1004, low_first("I", 4000000000))
    put_words(0xA8, 1006, low_first("f", 12.5))
    put_words(0xA8, 1010, low_first("d", 12.5))
    put_words(0xA8, 1014, low_first("q", -123456789012))
    put_words(0xA8, 1020, [0b1010])                       # D1020.1 and D1020.3 on
    put_words(0xB4, 0x1A, [1234])                         # W1A
    put_words(0xAF, 1000, [4321])                         # R1000
    for bit_code, number in ((0x90, 0), (0x9C, 0x1F), (0x9D, 0x10), (0x91, 400)):
        mem[bit_code][number] = 1                         # M0, X1F, Y10, SM400 on
    return mem


def mc_answer(request: bytes, mem: dict, counters: int) -> bytes:
    """Answers one 3E binary batch read (0401): D0.. are counters; D/W/R at or above 20000 -> C056."""
    command = request[11] | request[12] << 8
    sub = request[13] | request[14] << 8
    start = request[15] | request[16] << 8 | request[17] << 16
    code = request[18]
    count = request[19] | request[20] << 8
    head = bytes([0xD0, 0x00, request[2], request[3], request[4], request[5], request[6]])
    if command != 0x0401 or code not in MC_CODES or sub not in (0, 1) or (code not in MC_BIT_CODES and sub == 1):
        body = struct.pack("<H", 0xC059) + bytes(9)
    elif start + count > 20000 and code in (0xA8, 0xB4, 0xAF):
        body = struct.pack("<H", 0xC056) + bytes(9)
    elif sub == 1:
        bits = [mem[code].get(start + i, 0) for i in range(count)] + [0]
        body = b"\0\0" + bytes((bits[i] << 4) | bits[i + 1] for i in range(0, count, 2))
    else:
        words = []
        for i in range(count):
            n = start + i
            if code in MC_BIT_CODES:
                words.append(sum(mem[code].get(n * 16 + b, 0) << b for b in range(16)))
            elif code == 0xA8 and n < counters:
                words.append(counter(n))
            else:
                words.append(mem[code].get(n, 0))
        body = b"\0\0" + b"".join(struct.pack("<H", w) for w in words)
    return head + struct.pack("<H", len(body)) + body


def run_mc(port: int, counters: int) -> None:
    """Mitsubishi MC 3E binary server (batch read only), like an iQ-R/Q PLC's MC protocol port."""
    mem = mc_memory()

    async def handle(reader, writer):
        try:
            while True:
                header = await reader.readexactly(9)
                length = header[7] | header[8] << 8
                rest = await reader.readexactly(length)
                writer.write(mc_answer(header + rest, mem, counters))
                await writer.drain()
        except (asyncio.IncompleteReadError, ConnectionError):
            writer.close()

    async def main():
        server = await asyncio.start_server(handle, "127.0.0.1", port)
        async with server:
            await server.serve_forever()

    asyncio.run(main())


# ----------------------------------------------------------------------------- Siemens S7
def run_s7(port: int, counters: int) -> None:
    import snap7
    from snap7.type import SrvArea

    size = 512
    db1 = (ctypes.c_uint8 * size)()

    def put(offset: int, data: bytes) -> None:
        for i, byte in enumerate(data):
            db1[offset + i] = byte

    put(400, struct.pack(">H", 1234))
    put(402, struct.pack(">h", -1234))
    put(404, struct.pack(">i", -123456))
    put(408, struct.pack(">I", 4000000000))
    put(412, struct.pack(">f", 12.5))
    db1[416] = 0b0000_1001  # DBX416.0 and DBX416.3 true

    # Inputs, outputs and bit memory: I0.1, IW2 = -1234, Q1.7, QW4 = 1234, MD4 = 12.5, M8.0.
    inputs = (ctypes.c_uint8 * 16)(*([0b10, 0] + list(struct.pack(">h", -1234)) + [0] * 12))
    outputs = (ctypes.c_uint8 * 16)(*([0, 0b1000_0000, 0, 0] + list(struct.pack(">H", 1234)) + [0] * 10))
    merkers = (ctypes.c_uint8 * 16)(*([0] * 4 + list(struct.pack(">f", 12.5)) + [1] + [0] * 7))

    server = snap7.server.Server()
    server.register_area(SrvArea.DB, 1, db1)
    server.register_area(SrvArea.PE, 0, inputs)
    server.register_area(SrvArea.PA, 0, outputs)
    server.register_area(SrvArea.MK, 0, merkers)
    server.start(tcp_port=port)
    try:
        while True:
            for k in range(counters):
                put(2 * k, struct.pack(">H", counter(k)))
            time.sleep(0.2)
    finally:
        server.stop()
        server.destroy()


# ----------------------------------------------------------------------------- OPC UA
def run_opcua(port: int, counters: int, pki: str, user: str | None = None, password: str | None = None) -> None:
    from pathlib import Path
    from asyncua import Server, ua
    from asyncua.crypto.cert_gen import generate_private_key, generate_self_signed_app_certificate, dump_private_key_as_pem
    from cryptography.hazmat.primitives.serialization import Encoding
    from cryptography.x509.oid import ExtendedKeyUsageOID

    uri = "urn:prognode:soak:opcua"
    folder = Path(pki)
    folder.mkdir(parents=True, exist_ok=True)
    cert_path, key_path = folder / "server-cert.der", folder / "server-key.pem"
    if not cert_path.exists():
        key = generate_private_key()
        cert = generate_self_signed_app_certificate(
            key, "PROGNODE soak OPC UA", {"commonName": "PROGNODE soak OPC UA"}, [
                __import__("cryptography").x509.UniformResourceIdentifier(uri),
                __import__("cryptography").x509.DNSName("localhost"),
                # For localhost/127.0.0.1 the OPC UA .NET stack Core uses looks for this PC's name.
                __import__("cryptography").x509.DNSName(__import__("socket").gethostname()),
                __import__("cryptography").x509.IPAddress(__import__("ipaddress").ip_address("127.0.0.1")),
            ], extended=[ExtendedKeyUsageOID.SERVER_AUTH, ExtendedKeyUsageOID.CLIENT_AUTH])
        cert_path.write_bytes(cert.public_bytes(Encoding.DER))
        key_path.write_bytes(dump_private_key_as_pem(key))

    from asyncua.server.user_managers import UserManager
    from asyncua.server.user_managers import User, UserRole

    class OneUser(UserManager):
        """Like a site server with anonymous login turned off."""
        def get_user(self, iserver, username=None, password=None, certificate=None):
            if user is None:
                return User(role=UserRole.User)
            return User(role=UserRole.User) if username == user and password == password_ else None

    password_ = password

    async def main():
        server = Server(user_manager=OneUser())
        await server.init()
        if user is not None:
            server.set_security_IDs(["Username"])
        server.set_endpoint(f"opc.tcp://127.0.0.1:{port}/prognode-soak/")
        server.set_server_name("PROGNODE soak OPC UA")
        await server.set_application_uri(uri)
        server.set_security_policy([ua.SecurityPolicyType.Basic256Sha256_SignAndEncrypt])
        await server.load_certificate(str(cert_path))
        await server.load_private_key(str(key_path))
        idx = await server.register_namespace("urn:prognode:soak")
        objects = server.nodes.objects
        nodes = []
        for k in range(counters):
            nodes.append(await objects.add_variable(ua.NodeId(f"Counter{k}", idx), f"Counter{k}",
                                                    ua.Variant(0, ua.VariantType.UInt16)))
        fixed = [("Word", 1234, ua.VariantType.UInt16), ("Int16", -1234, ua.VariantType.Int16),
                 ("Int32", -123456, ua.VariantType.Int32), ("UInt32", 4000000000, ua.VariantType.UInt32),
                 ("Float", 12.5, ua.VariantType.Float), ("Bool", True, ua.VariantType.Boolean),
                 ("Double", 12.5, ua.VariantType.Double), ("Int64", -123456789012, ua.VariantType.Int64)]
        for name, value, vtype in fixed:
            await objects.add_variable(ua.NodeId(f"Fixed{name}", idx), f"Fixed{name}", ua.Variant(value, vtype))
        async with server:
            while True:
                for k, node in enumerate(nodes):
                    await node.write_value(ua.Variant(counter(k), ua.VariantType.UInt16))
                await asyncio.sleep(0.5)

    asyncio.run(main())


# ----------------------------------------------------------------------------- MQTT publisher
def run_mqtt(port: int, counters: int, user: str | None = None, password: str | None = None) -> None:
    import json
    import paho.mqtt.client as mqtt

    client = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id="prognode-soak-publisher")
    if user is not None:
        client.username_pw_set(user, password)
    client.reconnect_delay_set(min_delay=1, max_delay=2)
    connected = threading.Event()
    client.on_connect = lambda c, u, f, rc, p=None: connected.set()
    client.on_disconnect = lambda c, u, f, rc, p=None: connected.clear()
    while True:
        try:
            client.connect("127.0.0.1", port, keepalive=10)
            break
        except OSError:
            time.sleep(1)
    client.loop_start()
    while True:
        if connected.is_set():
            for k in range(counters):
                client.publish(f"soak/c/{k}", str(counter(k)), qos=0)
            client.publish("soak/s/word", "1234", qos=1, retain=True)
            client.publish("soak/s/float", "12.5", qos=1, retain=True)
            client.publish("soak/s/bool", "true", qos=1, retain=True)
            client.publish("soak/s/json", json.dumps({"temp": 12.5, "energy": 5000000000,
                                                       "sensors": [{"value": 1}, {"value": -123456}]}), qos=0)
        time.sleep(0.5)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("kind", choices=["modbus", "modbus-rtu", "mc", "s7", "opcua", "mqtt"])
    parser.add_argument("--port", type=int, default=0)
    parser.add_argument("--serial", default=r"\\.\CNCB0", help="modbus-rtu: serial port of the slave end")
    parser.add_argument("--counters", type=int, default=100)
    parser.add_argument("--pki", default="soak-pki")
    parser.add_argument("--user", help="OPC UA login / MQTT broker user (anonymous when omitted)")
    parser.add_argument("--password")
    a = parser.parse_args()
    print(f"simulator {a.kind} on {a.port} with {a.counters} counters", flush=True)
    {"modbus": lambda: run_modbus(a.port, a.counters), "modbus-rtu": lambda: run_modbus_rtu(a.serial, a.counters),
     "mc": lambda: run_mc(a.port, a.counters),
     "s7": lambda: run_s7(a.port, a.counters),
     "opcua": lambda: run_opcua(a.port, a.counters, a.pki, a.user, a.password),
     "mqtt": lambda: run_mqtt(a.port, a.counters, a.user, a.password)}[a.kind]()
    sys.exit(0)
