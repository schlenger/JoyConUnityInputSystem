namespace UnityEngine.InputSystem.Switch
{
    // Output reports: what can be sent to the controller.
    public abstract partial class SwitchControllerHID
    {
        // Number of the output report, increased with every report sent to this controller
        private byte m_packetNumber;

        /// <summary>
        /// Send an output report to the controller. Every report carries rumble data and a subcommand,
        /// the neutral ones are sent for whichever is not given.
        /// </summary>
        /// <param name="description">What the report does, for the error logged when sending it fails.</param>
        private bool SendCommand(string description, SwitchControllerBaseSubcommand subcommand = null, SwitchControllerRumbleProfile? rumbleProfile = null)
        {
            var c = SwitchControllerCommand.Create(m_packetNumber++, rumbleProfile, subcommand);
            if (ExecuteCommand(ref c) >= 0)
                return true;

            Debug.LogError($"{description} failed");
            return false;
        }

        private void ReadSPIFlash(SPIFlashReadAddressEnum region, string description)
        {
            LogVerbose($"Requesting {description}...");
            var readSubcommand = new SwitchControllerReadSPIFlashSubcommand(atAddress: (uint)region, withLength: SPIFlashRegion.Length(region));
            SendCommand($"Read {description}", readSubcommand);
        }

        public void Rumble(SwitchControllerRumbleProfile rumbleProfile)
        {
            SendCommand("Rumble command", rumbleProfile: rumbleProfile);
        }

        public void ReadControllerInfo()
        {
            LogVerbose("Requesting device info...");
            SendCommand("Request device info", new SwitchControllerRequestInfoSubcommand());
        }

        public void SetInputReportMode(InputModeEnum mode)
        {
            SendCommand($"Set report mode to {mode}", new SwitchControllerInputModeSubcommand(mode));
            m_isInStandardReportMode = (mode == InputModeEnum.Standard);
        }

        public void SetIMUEnabled(bool active)
        {
            SendCommand($"Set IMU active to {active}", new SwitchControllerSetImuEnabledSubcommand { Enabled = active });
        }

        public void SetVibrationEnabled(bool active)
        {
            SendCommand($"Set vibration active to {active}", new SwitchControllerSetVibrationEnabledSubcommand { Enabled = active });
        }

        public void SetLEDs(
            LEDStatusEnum p1 = LEDStatusEnum.Off,
            LEDStatusEnum p2 = LEDStatusEnum.Off,
            LEDStatusEnum p3 = LEDStatusEnum.Off,
            LEDStatusEnum p4 = LEDStatusEnum.Off)
        {
            SendCommand("Set LEDs", new SwitchControllerSetLEDSubcommand(p1, p2, p3, p4));
        }

        public void ReadFactoryIMUCalibrationData() => ReadSPIFlash(SPIFlashReadAddressEnum.FactoryIMUCalibration, "factory IMU calibration");

        public void ReadUserIMUCalibrationData() => ReadSPIFlash(SPIFlashReadAddressEnum.UserIMUCalibration, "user IMU calibration");

        public void ReadColors() => ReadSPIFlash(SPIFlashReadAddressEnum.ColorData, "colors");

        public void ReadStickCalibrationData() => ReadSPIFlash(SPIFlashReadAddressEnum.FactoryStickCalibration, "factory stick calibration");

        public void ReadSerialNumber() => ReadSPIFlash(SPIFlashReadAddressEnum.SerialNumber, "serial number");
    }
}
