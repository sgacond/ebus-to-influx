namespace EBusToInflux;

/// <summary>A known parameter of the heat pump. Label is the text on the control unit.</summary>
internal sealed record WeishauptParameter(string Name, string Label, string Unit, int Divisor = 10, bool Confirmed = true);

/// <summary>
/// Parameters of the WWP L 15 ARS-HE with its room control unit (WP-FS 380.02, WP 46.02).
/// Confirmed: value changed when changed on the control unit, or read exactly while its menu page was shown
/// and the value is unique. Not confirmed: matched by value and position in the settings block only.
/// </summary>
internal static class WeishauptParameters
{
    const string Celsius = "°C";

    public static readonly IReadOnlyDictionary<ushort, WeishauptParameter> Known = new Dictionary<ushort, WeishauptParameter>
    {
        // read by the control unit from the heat pump (50 22), mostly Systeminfo
        [0x0641] = new("OutsideTemperature", "Außen TO", Celsius),
        [0x0642] = new("EvaporatorTemperature", "Verdampfung TE", Celsius),
        [0x0643] = new("SuctionGasTemperature", "Sauggas TS", Celsius),
        [0x0644] = new("DischargeGasTemperature", "Druckgas TD", Celsius),
        [0x0645] = new("HeatPumpState", "Status WP", "", Divisor: 1), // 0 = Aus, 3 = Heizen
        [0x0646] = new("MaxFrequency", "Max Frequenz", "Hz"),
        [0x0647] = new("TargetFrequency", "Sollfrequenz", "Hz"),
        [0x0648] = new("CompressorFrequency", "Frequenz", "Hz"),
        [0x0649] = new("HeatPumpPower", "Leistung WP", "", Divisor: 1, Confirmed: false), // 15, display showed ----
        [0x064A] = new("OperatingModeActive", "Betriebsart", "", Divisor: 1, Confirmed: false), // 3
        [0x064B] = new("VersionWpMajor", "Version WP", "", Divisor: 1), // 46 of 46.02
        [0x064C] = new("VersionWpMinor", "Version WP", "", Divisor: 1), // 02 of 46.02
        [0x064D] = new("FlowTargetHc1", "Solltemp VL HK 1", Celsius),
        [0x064E] = new("FlowTargetHc2", "Solltemp VL HK 2", Celsius),
        [0x064F] = new("Flow", "Durchfluss", "", Divisor: 1), // 1 = Ja
        [0x0650] = new("CompressorRuntime", "Laufzeit Verdichter", "h", Divisor: 1),
        [0x0651] = new("RuntimePumpHc", "Laufzeit Pumpe HK", "h", Divisor: 1),
        [0x0652] = new("RuntimeElectricHeater1", "Laufzeit E Heiz 1", "h", Divisor: 1),
        [0x0653] = new("RuntimeElectricHeater2", "Laufzeit E Heiz 2", "h", Divisor: 1),
        [0x0654] = new("FlowTarget", "Solltemp VL", Celsius),
        [0x0655] = new("FlowTemperature", "Vorlauftemp", Celsius),
        [0x0656] = new("CondenserTemperature", "Verfl Temp", Celsius),
        [0x0657] = new("RefrigerantTemperature", "Kältemitteltemp", Celsius),
        [0x0658] = new("HotWaterTemperature", "WW Temp", Celsius),
        [0x0659] = new("BufferTemperature", "Weichentemp", Celsius, Confirmed: false),
        [0x065A] = new("FlowTemperatureHc1", "Vorlauftemp 1", Celsius, Confirmed: false),
        [0x065B] = new("FlowTemperatureHc2", "Vorlauftemp 2", Celsius, Confirmed: false),

        // written by the control unit to the heat pump (50 23), settings block 1701-1804
        [0x06A5] = new("OperatingMode", "Betriebsart", "", Divisor: 1), // 3 = Zeitprogramm 3
        [0x06A8] = new("RoomTemperature", "Raumtemperatur", Celsius),
        [0x06B1] = new("HotWaterTarget", "WW Solltemp", Celsius),
        [0x06B6] = new("HeatPumpPowerLimit", "WP Leistung", "%"),
        [0x06B8] = new("FlowOffsetHc1Heating", "+/- VL HK 1 Heiz", Celsius),
        [0x06BF] = new("RoomTargetHeating", "Raumsoll Heiz", Celsius, Confirmed: false),
        [0x06C1] = new("ReducedRoomTargetHeating", "Absenk Raum Heiz", Celsius),
        [0x06C2] = new("RoomTargetCooling", "Raumsoll Kühl", Celsius),
        [0x06C4] = new("ReducedRoomTargetCooling", "Absenk Raum Kühl", Celsius, Confirmed: false),
        [0x06D1] = new("BivalenceTemperature", "Bivalenztemp", Celsius, Confirmed: false), // or 0x06D5
        [0x06D4] = new("SecondHeaterFlowHysteresis", "Hysterese VL", "K", Confirmed: false),
        [0x06D5] = new("HeatPumpLimitTemperature", "Grenztemp WP", Celsius, Confirmed: false), // or 0x06D1
        [0x06D6] = new("SecondHeaterDelay", "Verzögerung", "min", Divisor: 1, Confirmed: false),
        [0x06DD] = new("DisinfectionTarget", "Therm Desinfekt Solltemp", Celsius),
        [0x06DF] = new("DisinfectionHour", "Therm Desinfekt Uhrzeit", "h", Divisor: 1),
        [0x06E0] = new("DisinfectionDuration", "Therm Desinfekt Dauer", "min", Divisor: 1),
        [0x06E1] = new("DisinfectionMaxTime", "Therm Desinfekt Max Zeit", "h", Divisor: 1),
        [0x06EB] = new("HeatingCurveOutsideMin", "Heizkennlinie Aussen Min", Celsius),
        [0x06EC] = new("HeatingCurveOutsideMax", "Heizkennlinie Aussen Max", Celsius, Confirmed: false),
        [0x06ED] = new("HeatingCurveMinFlowHc1", "Heizkennlinie Min Vorlauf HK 1", Celsius),
        [0x06EE] = new("HeatingCurveMaxFlowHc1", "Heizkennlinie Max Vorlauf HK 1", Celsius),
        [0x06EF] = new("CoolingCurveOutsideMax", "Kühlkennlinie Aussen Max", Celsius),
        [0x06F0] = new("CoolingCurveOutsideMin", "Kühlkennlinie Aussen Min", Celsius, Confirmed: false),
        [0x06F1] = new("CoolingCurveMinFlowHc1", "Kühlkennlinie Min Vorlauf HK 1", Celsius, Confirmed: false),
        [0x06F2] = new("CoolingCurveMaxFlowHc1", "Kühlkennlinie Max Vorlauf HK 1", Celsius, Confirmed: false),
        [0x06F3] = new("CoolingCurveMinFlowHc2", "Kühlkennlinie Min Vorlauf HK 2", Celsius, Confirmed: false),
        [0x06F4] = new("CoolingCurveMaxFlowHc2", "Kühlkennlinie Max Vorlauf HK 2", Celsius, Confirmed: false),
        [0x06F5] = new("HeatingCurveMinFlowHc2", "Heizkennlinie Min Vorlauf HK 2", Celsius),
        [0x06F6] = new("HeatingCurveMaxFlowHc2", "Heizkennlinie Max Vorlauf HK 2", Celsius),
    };

    public static WeishauptParameter? Find(ushort id) => Known.GetValueOrDefault(id);
}
