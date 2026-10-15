"""Checks the MC simulator with an independent client (pymcprotocol), so the simulator that tests
PROGNODE's Mitsubishi reader is not only checked by that reader. Usage: check_mc_simulator.py <port>"""
import struct
import sys
import time

import pymcprotocol


def main(port: int) -> None:
    # Q-compatible 3E binary (subcommands 0000/0001), which Q, L, iQ-R and FX5 all accept.
    plc = pymcprotocol.Type3E(plctype="Q")
    plc.setaccessopt(commtype="binary")
    plc.connect("127.0.0.1", port)
    words = plc.batchread_wordunits(headdevice="D1000", readsize=18)
    as_bytes = b"".join(struct.pack("<H", w & 0xFFFF) for w in words)
    checks = {
        "D1000 word": words[0] == 1234,
        "D1001 int16": struct.unpack("<h", as_bytes[2:4])[0] == -1234,
        "D1002 int32 low word first": struct.unpack("<i", as_bytes[4:8])[0] == -123456,
        "D1004 uint32": struct.unpack("<I", as_bytes[8:12])[0] == 4000000000,
        "D1006 float32": struct.unpack("<f", as_bytes[12:16])[0] == 12.5,
        "D1010 float64": struct.unpack("<d", as_bytes[20:28])[0] == 12.5,
        "D1014 int64": struct.unpack("<q", as_bytes[28:36])[0] == -123456789012,
    }
    bits = plc.batchread_bitunits(headdevice="M0", readsize=3)
    checks["M0..M2 bits"] = list(bits) == [1, 0, 0]
    checks["X1F hex bit"] = plc.batchread_bitunits(headdevice="X1F", readsize=1)[0] == 1
    checks["SM400"] = plc.batchread_bitunits(headdevice="SM400", readsize=1)[0] == 1
    checks["W1A hex word"] = plc.batchread_wordunits(headdevice="W1A", readsize=1)[0] == 1234
    checks["R1000"] = plc.batchread_wordunits(headdevice="R1000", readsize=1)[0] == 4321
    counter0 = plc.batchread_wordunits(headdevice="D0", readsize=1)[0]
    checks["D0 counter"] = abs(counter0 - int(time.time()) % 30000) <= 2
    try:
        plc.batchread_wordunits(headdevice="D19999", readsize=2)
        checks["out of range -> C056"] = False
    except Exception as error:  # pymcprotocol raises on a non-zero end code
        checks["out of range -> C056"] = "C056" in str(error).upper() or "0XC056" in str(error).upper() or "49238" in str(error)
        if not checks["out of range -> C056"]:
            print("end code error was:", error)
    plc.close()
    for name, ok in checks.items():
        print(("PASS " if ok else "FAIL ") + name)
    sys.exit(0 if all(checks.values()) else 1)


if __name__ == "__main__":
    main(int(sys.argv[1]))
