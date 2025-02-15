﻿using System.Collections.Generic;
using WindowsInput.Events;

namespace ControllerCommon.Devices
{
    public class AYANEONEXT : Device
    {
        public AYANEONEXT() : base()
        {
            this.ProductSupported = true;

            // device specific settings
            this.WidthHeightRatio = 2.4f;
            this.ProductIllustration = "device_aya_next";
            this.ProductModel = "AYANEONext";

            // https://www.amd.com/fr/products/apu/amd-ryzen-7-5800u
            // https://www.amd.com/fr/products/apu/amd-ryzen-7-5825u
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

            listeners.Add(new DeviceChord("Custom key BIG", new List<KeyCode>() { KeyCode.RControlKey, KeyCode.LWin, KeyCode.F12 }));
            listeners.Add(new DeviceChord("Custom key Small", new List<KeyCode>() { KeyCode.LWin, KeyCode.D }));
        }
    }
}
