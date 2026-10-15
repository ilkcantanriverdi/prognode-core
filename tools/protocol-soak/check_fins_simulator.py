"""Checks the Omron FINS simulator with an independent client (the `fins` package, used here only as a
test tool), so the simulator that tests PROGNODE's Omron reader is not only checked by that reader.
Also prints the library's request frame so it can be compared with PROGNODE's. Usage: check_fins_simulator.py <port>"""
import sys
import time

from fins.tcp import TCPFinsConnection

D, CIO, W, H, A, TC, TC_FLAG, E0 = b"\x82", b"\xB0", b"\xB1", b"\xB2", b"\xB3", b"\x89", b"\x09", b"\xA0"


def addr(word: int) -> bytes:
    return word.to_bytes(2, "big") + b"\x00"


def main(port: int) -> None:
    plc = TCPFinsConnection()
    plc.connect("127.0.0.1", port)
    print("library D1000 x18 request:", plc.fins_command_frame(b"\x01\x01", D + addr(1000) + (18).to_bytes(2, "big")).hex(" "))
    checks = {
        "handshake nodes": (plc.srce_node_add, plc.dest_node_add) == (0xEF, 1),
        "D1000 word": plc.get_values(">H", D, addr(1000), 1) == 1234,
        "D1001 int16": plc.get_values(">h", D, addr(1001), 1) == -1234,
        "D1002 int32 low word first": plc.get_values(">i", D, addr(1002), 2) == -123456,
        "D1004 uint32": plc.get_values(">I", D, addr(1004), 2) == 4000000000,
        "D1006 real": plc.get_values(">f", D, addr(1006), 2) == 12.5,
        "D1010 lreal": plc.get_values(">d", D, addr(1010), 4) == 12.5,
        "D1014 lint": plc.get_values(">q", D, addr(1014), 4) == -123456789012,
        "D1020 bits": plc.get_values(">H", D, addr(1020), 1) == 0b1010,
        "W10": plc.get_values(">H", W, addr(10), 1) == 1234,
        "H5": plc.get_values(">H", H, addr(5), 1) == 4321,
        "A20": plc.get_values(">H", A, addr(20), 1) == 42,
        "CIO100.07": plc.get_values(">H", CIO, addr(100), 1) & 0x80 != 0,
        "E0_100": plc.get_values(">H", E0, addr(100), 1) == 777,
        "T0 PV": plc.get_values(">H", TC, addr(0), 1) == 50,
        "C0 PV": plc.get_values(">H", TC, addr(0x8000), 1) == 7,
    }
    flags = plc.memory_area_read(TC_FLAG, addr(0), 1)
    checks["T0 done flag"] = flags[-1:] == b"\x01" and flags[12:14] == b"\0\0"
    flags = plc.memory_area_read(TC_FLAG, addr(0x8000), 1)
    checks["C0 flag off"] = flags[-1:] == b"\x00"
    counter0 = plc.get_values(">H", D, addr(0), 1)
    checks["D0 counter"] = abs(counter0 - int(time.time()) % 30000) <= 2
    refused = plc.memory_area_read(D, addr(19999), 2)
    checks["out of range -> 1103"] = refused[12:14] == b"\x11\x03"
    for name, ok in checks.items():
        print(("PASS " if ok else "FAIL ") + name)
    sys.exit(0 if all(checks.values()) else 1)


if __name__ == "__main__":
    main(int(sys.argv[1]))
