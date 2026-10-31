# EBusToInflux

Small .NET app that listens on the eBus of our Weishaupt heat pump and decodes the values. The plan is to write them to InfluxDB, and maybe to control the heating later.

There is no public eBus definition for this system, so everything below was reverse engineered by listening to the bus and comparing with the values on the control unit.

## Running

```
dotnet run --project EBusToInflux
```

Connects to the coupler (`ebus` section in `EBusToInflux/defaultConfig.json`, overridable via environment variables, e.g. `ebus__host`). It prints one line per value when it is first seen and whenever it changes:

```
18:12:03  5023  0x06C2 ( 1730)  RoomTargetCooling       240     24.0   (was 23.5)
```

Columns: time, eBus command, parameter id (hex and decimal), known name, raw value, value / 10, previous value.

The app only listens. **Anything written to the coupler's TCP port goes directly onto the heating bus.**

## Hardware

| Device | Details |
|---|---|
| Heat pump | Weishaupt **WWP L 15 ARS-HE**, air/water, inverter, 2 × 3 kW backup heaters |
| Hot water tank | Weishaupt **WAC 300**, 300 l. Not on the bus. Its sensor is wired to the heat pump |
| Control unit | eBus room control unit in the living room. The only operating panel; the heat pump itself has none |
| eBus coupler | ESERA **EBC-200** eBus Koppler Ethernet (Art. 12002), see below |

### eBus participants

| Master | Slave | Probably |
|---|---|---|
| `F1` | `F6` | Heat pump. `F6` answers all `50 22` reads |
| `30` | (`35`) | Control unit in the living room. Reads from `F6`, writes settings to `F1` |
| `07` | (`0C`) | Unknown, reads the same values as `30`. Maybe the "sekundärer Wärmeerzeuger" (backup heater) |

Slave address = master address + 5 (eBus convention).

### ESERA coupler

The coupler is a **WIZnet WIZ750SR** serial-to-Ethernet module (MAC `00:08:DC:67:38:45`, DHCP, `ebus.gacond.local`). Its operating mode is **TCP server, port 5000**, and it streams the raw bus bytes.

It can be configured without the Windows "ESERA CONFIG TOOL 1" (the protocol was taken from decompiling the tool). Send UDP to port **50001**, as broadcast or directly:

```
"MA" + 6 MAC bytes (FF FF FF FF FF FF = all) + "\r\n"
"PW \r\n"                       (empty password)
commands, each "XX[value]\r\n"
```

- Query: command without value, e.g. `MC`, `VR`, `OP`, `LI`, `LP`, `RH`, `RP`, `ST`
- Write: e.g. `OP1\r\nLP5000\r\n`, then `SV\r\n` (save) and `RT\r\n` (restart)
- `OP`: 0 = TCP client, 1 = TCP server, 2 = TCP mixed, 3 = UDP. `IM`: 0 = static, 1 = DHCP

Known issue: on 2026-10-31 port 5000 refused all connections although the module reported `OP1`, `LP5000` and `ST OPEN`. A power cycle did not help. Writing the same settings again with `SV` + `RT` fixed it.

## eBus basics (as implemented in `TelegramParser`)

```
QQ ZZ PB SB NN D1..Dn CRC   ACK   [NN D1..Dn CRC   ACK]   SYN
master part                 slave answer (only if ZZ is a slave address)
```

- `AA` = SYN (idle / end of telegram), `A9` = escape: `A9 00` → `A9`, `A9 01` → `AA`
- CRC8, polynomial `0x9B`, over the escaped bytes (`crc = table[crc] ^ byte`)
- `ZZ = FE`: broadcast, no ACK. `ZZ` = master address: ACK only, no answer
- Master addresses have both nibbles in `{0, 1, 3, 7, F}`

## Weishaupt protocol (`WeishauptDecoder`)

The same protocol is documented for Weishaupt WTC boilers in
[B-o-r-i-s-M/Weishaupt-WTC-OB-ebusd-Home-Assistant](https://github.com/B-o-r-i-s-M/Weishaupt-WTC-OB-ebusd-Home-Assistant/blob/main/docs/03-protokoll.md). The parameter numbers of the heat pump are different, though.

```
50 22  read parameter:   CK  ID_lo ID_hi                            → answer: VAL_lo VAL_hi
50 23  write parameter:  CK  ID_lo ID_hi  VAL_lo VAL_hi  5D 01 00 00 (no answer, ACK only)
```

- **ID**: 16-bit parameter number, little endian
- **VAL**: int16, little endian. Temperatures in 1/10 °C. `0x8000` = not available
- **`5D 01 00 00`**: constant, meaning unknown
- **CK**: check byte, CRC8 with polynomial `0x5C` over all following payload bytes, eBus-style (`crc = shift(crc) ^ byte`):

```csharp
byte crc = 0;
foreach (var b in data)          // data = payload without CK
{
    var c = crc;
    for (var bit = 0; bit < 8; bit++)
        c = (byte)((c & 0x80) != 0 ? (c << 1) ^ 0x5C : c << 1);
    crc = (byte)(c ^ b);
}
```

Observations:
- `30` and `07` poll a few values from `F6` with `50 22` (outside, flow and hot water temperature, heat pump state, frequency).
- `30` regularly writes some settings and the room temperature to `F1` with `50 23`.
- On opening the settings menu, `30` writes **all** settings (IDs 1702–1804) once. Any change on the control unit appears on the bus within seconds.
- According to the WTC project, a WCM-FS only accepts writes from sender `FF`. This has not been tested on this system.

## Known parameters

Confirmed by comparing with the control unit, or by changing the value and watching the bus:

| ID | Dec | Cmd | Name | Unit | Notes |
|---|---|---|---|---|---|
| `0x0641` | 1601 | read | Outside temperature | 0.1 °C | |
| `0x0645` | 1605 | read | Heat pump state | code | 0 = off, 3 = Heizen |
| `0x0648` | 1608 | read | Compressor frequency | 0.1 Hz | display shows whole Hz |
| `0x0655` | 1621 | read | Flow temperature (Vorlauf) | 0.1 °C | |
| `0x0658` | 1624 | read | Hot water temperature | 0.1 °C | sensor in the WAC 300 |
| `0x06A5` | 1701 | write | Operating mode | code | 3 = Zeitprogramm 3 |
| `0x06A7` | 1703 | write | Room target (active?) | 0.1 °C | followed room target 21.5 → 22.0 |
| `0x06A8` | 1704 | write | Room temperature | 0.1 °C | |
| `0x06B1` | 1713 | write | Hot water target | 0.1 °C | |
| `0x06B4` | 1716 | write | Room target (active?) | 0.1 °C | followed room target 21.5 → 22.0 |
| `0x06C1` | 1729 | write | Absenk Raum Heiz | 0.1 °C | |
| `0x06C2` | 1730 | write | Raum Soll Kühl | 0.1 °C | |

Likely, by value and position in the settings block:

| ID | Dec | Name | Value seen |
|---|---|---|---|
| `0x06BF` | 1727 | Raum Soll Heiz | 22.0 |
| `0x06C4` | 1732 | Absenk Raum Kühl | 28.0 |

Read values not yet identified: `0x0199` (409) = 38.1. `0x065E`, `0x065F`, `0x0663`, `0x0666` (1630, 1631, 1635, 1638) were 0, even while heating.

### Settings block (1702–1804), raw values on 2026-10-31

Raw values, not divided by 10. Heating active, mode Zeitprogramm 3, room target 22.0. Snapshot to match against the control unit menus. Also seen outside this block: `0x0285` (645) = 80.

| ID | Raw | ID | Raw | ID | Raw | ID | Raw |
|---|---|---|---|---|---|---|---|
| 1702 | 0 | 1728 | 160 | 1754 | 1 | 1780 | 180 |
| 1703 | 220 | 1729 | 160 | 1755 | 10 | 1781 | 200 |
| 1704 | 224 | 1730 | 235 | 1756 | 10 | 1782 | 350 |
| 1705 | 0 | 1731 | 300 | 1757 | 650 | 1783 | 5 |
| 1706 | 170 | 1732 | 280 | 1758 | 5 | 1784 | 15 |
| 1707 | 2 | 1733 | 0 | 1759 | 14 | 1785 | 0 |
| 1708 | 0 | 1734 | 0 | 1760 | 30 | 1786 | 165 |
| 1709 | 0 | 1735 | 0 | 1761 | 4 | 1787 | 1 |
| 1710 | 0 | 1736 | -8 | 1762 | 119 | 1788 | 0 |
| 1711 | 0 | 1737 | 0 | 1763 | 1 | 1789 | 0 |
| 1712 | 0 | 1738 | 2 | 1764 | 0 | 1790 | 1 |
| 1713 | 540 | 1739 | 0 | 1765 | 161 | 1791 | 0 |
| 1714 | 0 | 1740 | 0 | 1766 | 0 | 1792 | 0 |
| 1715 | 15 | 1741 | 31 | 1767 | 0 | 1793 | 0 |
| 1716 | 220 | 1742 | 100 | 1768 | 0 | 1794 | 0 |
| 1717 | 60 | 1743 | 40 | 1769 | 0 | 1795 | 1 |
| 1718 | 750 | 1744 | 0 | 1770 | 0 | 1796 | 0 |
| 1720 | -40 | 1745 | -50 | 1771 | -70 | 1797 | 0 |
| 1721 | 0 | 1746 | 2 | 1772 | 180 | 1798 | 242 |
| 1722 | 0 | 1747 | 1 | 1773 | 230 | 1800 | 1 |
| 1723 | 0 | 1748 | 30 | 1774 | 370 | 1801 | -200 |
| 1724 | 5 | 1749 | -50 | 1775 | 400 | 1802 | 240 |
| 1725 | 600 | 1750 | 10 | 1776 | 220 | 1803 | 240 |
| 1726 | 500 | 1751 | 540 | 1777 | 180 | 1804 | 1 |
| 1727 | 220 | 1752 | 5 | 1778 | 180 | | |
| | | 1753 | 5 | 1779 | 180 | | |

1719 and 1799 did not appear.

## Finding more parameters

1. Run the app and note the time.
2. Change a value on the control unit (in small, reversible steps) and write down the time.
3. The line with the new value names the parameter id. Add it to `WeishauptDecoder.KnownNames` and to this README.
