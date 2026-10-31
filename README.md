# EBusToInflux

Small .NET app that listens on the eBus of our Weishaupt heat pump and decodes the values. The plan is to write them to InfluxDB, and maybe to control the heating later.

There is no public eBus definition for this system, so everything below was reverse engineered by listening to the bus and comparing with the values on the control unit.

## Running

```
dotnet run --project EBusToInflux
```

Requires the .NET 10 SDK (see `global.json`). Tests: `dotnet test`.

The app is a .NET Generic Host with two background services, connected by a channel:

- **`EBusListener`** reads the coupler, decodes the Weishaupt telegrams and reconnects automatically. It prints one line per value when it is first seen and whenever it changes:
  ```
  18:12:03  5023  0x06C2 ( 1730)  RoomTargetCooling                240  24 °C      (was 23.5)
  ```
  Columns: time, eBus command, parameter id (hex and decimal), name (`?` = not confirmed), raw value, scaled value, previous value.
- **`InfluxWriter`** writes **measurements only** (not settings) to InfluxDB 2.x: immediately when a value changes, otherwise again every `RepeatInterval` (5 min) so graphs have no gaps. Each value is a field of the measurement `heatpump`, e.g. `heatpump OutsideTemperature=11.5`. Lines are batched every 10 s and kept for retry if InfluxDB is down.

Configuration is in `EBusToInflux/appsettings.json` (sections `EBus` and `Influx`). It can be overridden by environment variables (`EBus__Host`, `Influx__Token`, …) or command-line arguments (`--EBus:Host=192.168.15.244`). **Writing is only enabled when `Influx:Token` is set.** The token never goes into the repo; use a token with write access to the bucket `home` only.

The coupler accepts only **one** TCP client at a time, so stop other listeners first. The app only listens. **Anything written to the coupler's TCP port goes directly onto the heating bus.**

Note: in normal operation the control unit only polls outside, flow, hot water and room temperature, heat pump state and frequency. The other measurements (refrigerant circuit, run times, …) only appear on the bus while the *Systeminfo* menu is open.

### Docker (NAS)

```
cp .env.example .env    # and set INFLUX_TOKEN
docker compose up -d --build
```

If the `.local` host names don't resolve inside the container, set `EBus__Host` and `Influx__Url` to IP addresses in `compose.yaml`.

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

The catalog lives in `EBusToInflux/WeishauptParameters.cs`, with name, display label, unit and divisor for each parameter. The probe marks unconfirmed names with `?`.

**Confirmed** means the value changed on the bus when it was changed on the control unit, or the control unit read it exactly while its menu page was open and the value is unique. **Likely** means matched only by value and position in the settings block.

The control unit only reads the values it currently needs: a few in the background, the rest while the matching *Systeminfo* page is open.

### Read from the heat pump (`50 22`)

| ID | Dec | Label on control unit | Unit | ÷ | Status | Notes |
|---|---|---|---|---|---|---|
| `0x0641` | 1601 | Außen TO | °C | 10 | confirmed | |
| `0x0642` | 1602 | Verdampfung TE | °C | 10 | confirmed | |
| `0x0643` | 1603 | Sauggas TS | °C | 10 | confirmed | |
| `0x0644` | 1604 | Druckgas TD | °C | 10 | confirmed | |
| `0x0645` | 1605 | Status WP | code | 1 | confirmed | 0 = Aus, 3 = Heizen |
| `0x0646` | 1606 | Max Frequenz | Hz | 10 | confirmed | 70 Hz |
| `0x0647` | 1607 | Sollfrequenz | Hz | 10 | confirmed | |
| `0x0648` | 1608 | Frequenz | Hz | 10 | confirmed | display shows whole Hz |
| `0x0649` | 1609 | Leistung WP? | | | unclear | 15, display showed `----` |
| `0x064A` | 1610 | Betriebsart? | code | 1 | likely | 3 |
| `0x064B` | 1611 | Version WP (major) | | 1 | confirmed | 46 of 46.02 |
| `0x064C` | 1612 | Version WP (minor) | | 1 | confirmed | 02 of 46.02 |
| `0x064D` | 1613 | Solltemp VL HK 1 | °C | 10 | confirmed | |
| `0x064E` | 1614 | Solltemp VL HK 2 | °C | 10 | confirmed | |
| `0x064F` | 1615 | Durchfluss | bool | 1 | confirmed | 1 = Ja |
| `0x0650` | 1616 | Laufzeit Verdichter | h | 1 | confirmed | 28890 |
| `0x0651` | 1617 | Laufzeit Pumpe HK | h | 1 | confirmed | -1 |
| `0x0652` | 1618 | Laufzeit E Heiz 1 | h | 1 | confirmed | |
| `0x0653` | 1619 | Laufzeit E Heiz 2 | h | 1 | confirmed | |
| `0x0654` | 1620 | Solltemp VL | °C | 10 | confirmed | |
| `0x0655` | 1621 | Vorlauftemp | °C | 10 | confirmed | |
| `0x0656` | 1622 | Verfl Temp | °C | 10 | confirmed | condenser |
| `0x0657` | 1623 | Kältemitteltemp | °C | 10 | confirmed | |
| `0x0658` | 1624 | WW Temp | °C | 10 | confirmed | sensor in the WAC 300 |
| `0x0659`–`0x065B` | 1625–1627 | Weichentemp, Vorlauftemp 1, Vorlauftemp 2 | °C | 10 | likely | all 0, order assumed |

Not identified: `0x0199` (409) = 38.1; `0x065D` (1629) = 512; `0x065E`, `0x065F`, `0x0663`, `0x0666` (1630, 1631, 1635, 1638) = 0. Not found on the bus: Störmeldungen, and Version WP-FS (the control unit's own version, 380.02).

### Written by the control unit (`50 23`)

| ID | Dec | Label on control unit | Unit | ÷ | Status | Value seen |
|---|---|---|---|---|---|---|
| `0x06A5` | 1701 | Betriebsart | code | 1 | confirmed | 3 = Zeitprogramm 3 |
| `0x06A7` | 1703 | room target (active?) | °C | 10 | confirmed | follows Raumsoll Heiz |
| `0x06A8` | 1704 | room temperature | °C | 10 | confirmed | |
| `0x06B1` | 1713 | WW Solltemp | °C | 10 | confirmed | 54.0 |
| `0x06B4` | 1716 | room target (active?) | °C | 10 | confirmed | follows Raumsoll Heiz |
| `0x06B6` | 1718 | WP Leistung | % | 10 | confirmed | 75 |
| `0x06B8` | 1720 | +/- VL HK 1 Heiz | °C | 10 | confirmed | -4.0 |
| `0x06BF` | 1727 | Raumsoll Heiz | °C | 10 | confirmed | 22.0 |
| `0x06C1` | 1729 | Absenk Raum Heiz | °C | 10 | confirmed | 16.0 |
| `0x06C2` | 1730 | Raumsoll Kühl | °C | 10 | confirmed | 23.5 |
| `0x06C4` | 1732 | Absenk Raum Kühl | °C | 10 | confirmed | 28.0 |
| `0x06D1` | 1745 | Grenztemp WP | °C | 10 | confirmed (by elimination) | -5.0 |
| `0x06D4` | 1748 | Hysterese VL (2. WE) | K | 10 | confirmed | 3.0 |
| `0x06D5` | 1749 | Bivalenztemp | °C | 10 | confirmed | -5.0 |
| `0x06D6` | 1750 | Verzögerung (2. WE) | min | 1 | confirmed | 10 |
| `0x06DD` | 1757 | Therm Desinfekt Solltemp | °C | 10 | confirmed | 65.0 |
| `0x06DF` | 1759 | Therm Desinfekt Uhrzeit | h | 1 | confirmed | 14 |
| `0x06E0` | 1760 | Therm Desinfekt Dauer | min | 1 | confirmed | 30 |
| `0x06E1` | 1761 | Therm Desinfekt Max Zeit | h | 1 | confirmed | 4 |
| `0x06EB` | 1771 | Heizkennlinie Aussen Min | °C | 10 | confirmed | -7.0 |
| `0x06EC` | 1772 | Heizkennlinie Aussen Max | °C | 10 | confirmed | 18.0 |
| `0x06ED` | 1773 | Heizkennlinie Min Vorlauf HK 1 | °C | 10 | confirmed | 23.0 |
| `0x06EE` | 1774 | Heizkennlinie Max Vorlauf HK 1 | °C | 10 | confirmed | 37.0 |
| `0x06EF` | 1775 | Kühlkennlinie Aussen Max | °C | 10 | confirmed | 40.0 |
| `0x06F0` | 1776 | Kühlkennlinie Aussen Min | °C | 10 | confirmed | 22.0 |
| `0x06F1` | 1777 | Kühlkennlinie Min Vorlauf HK 1 | °C | 10 | confirmed | 18.0 |
| `0x06F2` | 1778 | Kühlkennlinie Max Vorlauf HK 1 | °C | 10 | confirmed | 18.0 |
| `0x06F3` | 1779 | Kühlkennlinie Min Vorlauf HK 2 | °C | 10 | confirmed | 18.0 |
| `0x06F4` | 1780 | Kühlkennlinie Max Vorlauf HK 2 | °C | 10 | confirmed (by elimination) | 18.0 |
| `0x06F5` | 1781 | Heizkennlinie Min Vorlauf HK 2 | °C | 10 | confirmed | 20.0 |
| `0x06F6` | 1782 | Heizkennlinie Max Vorlauf HK 2 | °C | 10 | confirmed | 35.0 |

Menu items not yet assigned (ambiguous values): +/- VL HK 1 Kühl (0.0), 2. Wärmeerzeuger (aktiv), 2. WE bei Störung (Nein), Anzahl 2. WE (02, candidates 1707, 1738, 1746), Prio (00), Zyklus (Therm Desinfekt).

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
