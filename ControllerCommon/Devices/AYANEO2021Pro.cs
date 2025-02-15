﻿using System.Collections.Generic;
using WindowsInput.Events;

namespace ControllerCommon.Devices
{
    public class AYANEO2021Pro : Device
    {
        public AYANEO2021Pro() : base()
        {
            this.ProductSupported = true;

            // device specific settings
            this.WidthHeightRatio = 2.4f;
            this.ProductIllustration = "device_aya_2021";
            this.ProductModel = "AYANEO2021";

            // https://www.amd.com/fr/products/apu/amd-ryzen-7-4800u
            this.nTDP = new double[] { 15, 15, 20 };
            this.cTDP = new double[] { 10, 25 };

            this.AngularVelocityAxisSwap = new()
            {
                { 'X', 'X' },
                { 'Y', 'Z' },
                { 'Z', 'Y' },
            };

            this.AccelerationAxisSwap = new()
            {
                { 'X', 'X' },
                { 'Y', 'Z' },
                { 'Z', 'Y' },
            };

            listeners.Add(new DeviceChord("WIN key", new List<KeyCode>() { KeyCode.LWin }));
            //listeners.Add("TM key", new ChordClick(KeyCode.RAlt, KeyCode.RControlKey, KeyCode.Delete)); // Conflicts with OS
            listeners.Add(new DeviceChord("ESC key", new List<KeyCode>() { KeyCode.Escape }));
            listeners.Add(new DeviceChord("KB key", new List<KeyCode>() { KeyCode.RControlKey, KeyCode.LWin })); // Conflicts with Ayaspace when installed
        }
    }
}
