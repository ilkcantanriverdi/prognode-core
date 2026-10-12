# Intended use and limitations

This statement is part of the product documentation, the end-user license agreement and the technical
documentation of PROGNODE. It applies to PROGNODE Core, the PROGNODE Client for Windows, the PROGNODE
mobile app and PROGNODE Cloud.

## Intended use

PROGNODE is **monitoring software** for industrial sites. It

- reads values from PLCs, meters and other industrial data sources over Modbus TCP, Siemens S7
  communication, OPC UA and MQTT;
- evaluates operator alarms on those values and notifies people on the Core PC, Windows PCs and phones;
- records selected values in a local historian and displays trends.

PROGNODE supports operators and maintenance teams in seeing process conditions earlier. It is intended
to be installed and configured by trained personnel within a plant network that is protected according
to the site's security concept (see the secure installation guidance in the user guide).

## Not intended for

- **Safety functions.** PROGNODE is not a safety instrumented system (SIS), not a safety-related system
  in the sense of IEC 61508 / IEC 61511, and it has no safety integrity level (SIL). It must not be used
  as a safety alarm, an interlock, an emergency shutdown, or the only means of protecting people,
  equipment or the environment.
- **Control.** All industrial connectors are read-only. PROGNODE does not write values, does not
  download, modify or execute PLC programs (IEC 61131-3 is therefore not applicable) and does not send
  commands to field devices. Acknowledging an alarm in PROGNODE confirms that it was seen in PROGNODE; it
  does not acknowledge, reset or change anything in the PLC.
- **Guaranteed delivery.** Notifications depend on networks, operating systems and third-party push
  services and can be delayed or lost. Critical conditions must also be signalled by the site's own
  alarm and safety systems.
- **Medical, aviation, nuclear or other uses where failure could directly lead to death or serious injury.**

## Conditions of use

- Keep the Core PC, its operating system and PROGNODE updated; install security updates promptly.
- Restrict network access to the Core PC to the plant network; enable LAN access only for the
  approved devices or subnet.
- Protect PROGNODE accounts with strong, unique passwords; remove paired devices that are no longer used.
- Verify device addresses, scaling and alarm limits during commissioning and after changes.
- Keep encrypted backups outside the Core PC.

## Standards referenced in this statement

| Standard | Applicability to PROGNODE |
|---|---|
| IEC 61508 / IEC 61511 | Not applicable — PROGNODE performs no safety function. |
| IEC 61131-3 | Not applicable — PROGNODE does not program or write to PLCs. |
| ISA-18.2 / IEC 62682 | Alarm management practices that customers apply with PROGNODE's alarm features. |
| IEC 62443-4-1 / 4-2 | Target for the PROGNODE development process and product security capabilities. |
