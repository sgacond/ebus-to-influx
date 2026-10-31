namespace EBusToInflux;

/// <summary>Measurements change during operation and are recorded; settings and info only change by hand.</summary>
internal enum ParameterKind { Measurement, Setting, Info }

/// <summary>A known parameter of the heat pump. Label is the text on the control unit.</summary>
internal sealed record WeishauptParameter(
    string Name, string Label, string Unit, ParameterKind Kind, int Divisor = 10, bool Confirmed = true);

/// <summary>
/// Parameters of the WWP L 15 ARS-HE with its room control unit (WP-FS 380.02, WP 46.02).
/// Confirmed: value changed when changed on the control unit, or read exactly while its menu page was shown
/// and the value is unique. Not confirmed: matched by value and position in the settings block only.
/// </summary>
internal static class WeishauptParameters
{
    const string Celsius = "°C";

    static WeishauptParameter Measurement(string name, string label, string unit, int divisor = 10, bool confirmed = true) =>
        new(name, label, unit, ParameterKind.Measurement, divisor, confirmed);

    static WeishauptParameter Setting(string name, string label, string unit, int divisor = 10, bool confirmed = true) =>
        new(name, label, unit, ParameterKind.Setting, divisor, confirmed);

    static WeishauptParameter Info(string name, string label, string unit, int divisor = 10) =>
        new(name, label, unit, ParameterKind.Info, divisor);

    public static readonly IReadOnlyDictionary<ushort, WeishauptParameter> Known = new Dictionary<ushort, WeishauptParameter>
    {
        // read by the control unit from the heat pump (50 22), mostly Systeminfo
        [0x0641] = Measurement("OutsideTemperature", "Außen TO", Celsius),
        [0x0642] = Measurement("EvaporatorTemperature", "Verdampfung TE", Celsius),
        [0x0643] = Measurement("SuctionGasTemperature", "Sauggas TS", Celsius),
        [0x0644] = Measurement("DischargeGasTemperature", "Druckgas TD", Celsius),
        [0x0645] = Measurement("HeatPumpState", "Status WP", "", divisor: 1), // 0 = Aus, 3 = Heizen
        [0x0646] = Info("MaxFrequency", "Max Frequenz", "Hz"),
        [0x0647] = Measurement("TargetFrequency", "Sollfrequenz", "Hz"),
        [0x0648] = Measurement("CompressorFrequency", "Frequenz", "Hz"),
        [0x0649] = Measurement("HeatPumpPower", "Leistung WP", "", divisor: 1, confirmed: false), // 15, display showed ----
        [0x064A] = Measurement("OperatingModeActive", "Betriebsart", "", divisor: 1, confirmed: false), // 3
        [0x064B] = Info("VersionWpMajor", "Version WP", "", divisor: 1), // 46 of 46.02
        [0x064C] = Info("VersionWpMinor", "Version WP", "", divisor: 1), // 02 of 46.02
        [0x064D] = Measurement("FlowTargetHc1", "Solltemp VL HK 1", Celsius),
        [0x064E] = Measurement("FlowTargetHc2", "Solltemp VL HK 2", Celsius),
        [0x064F] = Measurement("Flow", "Durchfluss", "", divisor: 1), // 1 = Ja
        [0x0650] = Measurement("CompressorRuntime", "Laufzeit Verdichter", "h", divisor: 1),
        [0x0651] = Measurement("RuntimePumpHc", "Laufzeit Pumpe HK", "h", divisor: 1),
        [0x0652] = Measurement("RuntimeElectricHeater1", "Laufzeit E Heiz 1", "h", divisor: 1),
        [0x0653] = Measurement("RuntimeElectricHeater2", "Laufzeit E Heiz 2", "h", divisor: 1),
        [0x0654] = Measurement("FlowTarget", "Solltemp VL", Celsius),
        [0x0655] = Measurement("FlowTemperature", "Vorlauftemp", Celsius),
        [0x0656] = Measurement("CondenserTemperature", "Verfl Temp", Celsius),
        [0x0657] = Measurement("RefrigerantTemperature", "Kältemitteltemp", Celsius),
        [0x0658] = Measurement("HotWaterTemperature", "WW Temp", Celsius),
        [0x0659] = Measurement("BufferTemperature", "Weichentemp", Celsius, confirmed: false),
        [0x065A] = Measurement("FlowTemperatureHc1", "Vorlauftemp 1", Celsius, confirmed: false),
        [0x065B] = Measurement("FlowTemperatureHc2", "Vorlauftemp 2", Celsius, confirmed: false),

        // written by the control unit to the heat pump (50 23), settings block 1701-1804
        [0x06A5] = Setting("OperatingMode", "Betriebsart", "", divisor: 1), // 3 = Zeitprogramm 3
        [0x06A7] = Measurement("RoomTargetActive", "aktiver Raumsoll", Celsius, confirmed: false), // follows Raumsoll Heiz, as does 0x06B4
        [0x06A8] = Measurement("RoomTemperature", "Raumtemperatur", Celsius),
        [0x06B1] = Setting("HotWaterTarget", "WW Solltemp", Celsius),
        [0x06B6] = Setting("HeatPumpPowerLimit", "WP Leistung", "%"),
        [0x06B8] = Setting("FlowOffsetHc1Heating", "+/- VL HK 1 Heiz", Celsius),
        [0x06BF] = Setting("RoomTargetHeating", "Raumsoll Heiz", Celsius),
        [0x06C1] = Setting("ReducedRoomTargetHeating", "Absenk Raum Heiz", Celsius),
        [0x06C2] = Setting("RoomTargetCooling", "Raumsoll Kühl", Celsius),
        [0x06C4] = Setting("ReducedRoomTargetCooling", "Absenk Raum Kühl", Celsius),
        [0x06D1] = Setting("HeatPumpLimitTemperature", "Grenztemp WP", Celsius), // by elimination
        [0x06D4] = Setting("SecondHeaterFlowHysteresis", "Hysterese VL", "K"),
        [0x06D5] = Setting("BivalenceTemperature", "Bivalenztemp", Celsius),
        [0x06D6] = Setting("SecondHeaterDelay", "Verzögerung", "min", divisor: 1),
        [0x06DD] = Setting("DisinfectionTarget", "Therm Desinfekt Solltemp", Celsius),
        [0x06DF] = Setting("DisinfectionHour", "Therm Desinfekt Uhrzeit", "h", divisor: 1),
        [0x06E0] = Setting("DisinfectionDuration", "Therm Desinfekt Dauer", "min", divisor: 1),
        [0x06E1] = Setting("DisinfectionMaxTime", "Therm Desinfekt Max Zeit", "h", divisor: 1),
        [0x06EB] = Setting("HeatingCurveOutsideMin", "Heizkennlinie Aussen Min", Celsius),
        [0x06EC] = Setting("HeatingCurveOutsideMax", "Heizkennlinie Aussen Max", Celsius),
        [0x06ED] = Setting("HeatingCurveMinFlowHc1", "Heizkennlinie Min Vorlauf HK 1", Celsius),
        [0x06EE] = Setting("HeatingCurveMaxFlowHc1", "Heizkennlinie Max Vorlauf HK 1", Celsius),
        [0x06EF] = Setting("CoolingCurveOutsideMax", "Kühlkennlinie Aussen Max", Celsius),
        [0x06F0] = Setting("CoolingCurveOutsideMin", "Kühlkennlinie Aussen Min", Celsius),
        [0x06F1] = Setting("CoolingCurveMinFlowHc1", "Kühlkennlinie Min Vorlauf HK 1", Celsius),
        [0x06F2] = Setting("CoolingCurveMaxFlowHc1", "Kühlkennlinie Max Vorlauf HK 1", Celsius),
        [0x06F3] = Setting("CoolingCurveMinFlowHc2", "Kühlkennlinie Min Vorlauf HK 2", Celsius),
        [0x06F4] = Setting("CoolingCurveMaxFlowHc2", "Kühlkennlinie Max Vorlauf HK 2", Celsius),
        [0x06F5] = Setting("HeatingCurveMinFlowHc2", "Heizkennlinie Min Vorlauf HK 2", Celsius),
        [0x06F6] = Setting("HeatingCurveMaxFlowHc2", "Heizkennlinie Max Vorlauf HK 2", Celsius),
    };

    public static WeishauptParameter? Find(ushort id) => Known.GetValueOrDefault(id);
}
