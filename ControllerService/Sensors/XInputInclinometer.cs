using ControllerCommon.Managers;
using ControllerCommon.Sensors;
using System;
using System.Numerics;
using Windows.Devices.Sensors;
using static ControllerCommon.Utils.DeviceUtils;

namespace ControllerService.Sensors
{
    public class XInputInclinometer : XInputSensor
    {
        public XInputInclinometer(SensorFamily sensorFamily, int updateInterval) : base()
        {
            this.updateInterval = updateInterval;
            UpdateSensor(sensorFamily);
        }

        public void UpdateSensor(SensorFamily sensorFamily)
        {
            switch (sensorFamily)
            {
                case SensorFamily.WindowsDevicesSensors:
                    sensor = Accelerometer.GetDefault();
                    break;
                case SensorFamily.SerialUSBIMU:
                    sensor = SerialUSBIMU.GetDefault();
                    break;
            }

            if (sensor == null)
            {
                LogManager.LogWarning("{0} not initialised as a {1}.", this.ToString(), sensorFamily.ToString());
                return;
            }

            switch (sensorFamily)
            {
                case SensorFamily.WindowsDevicesSensors:
                    ((Accelerometer)sensor).ReportInterval = (uint)updateInterval;
                    ((Accelerometer)sensor).ReadingChanged += ReadingChanged;
                    filter.SetFilterAttrs(ControllerService.handheldDevice.oneEuroSettings.minCutoff, ControllerService.handheldDevice.oneEuroSettings.beta);

                    LogManager.LogInformation("{0} initialised as a {1}. Report interval set to {2}ms", this.ToString(), sensorFamily.ToString(), updateInterval);
                    break;
                case SensorFamily.SerialUSBIMU:
                    ((SerialUSBIMU)sensor).ReadingChanged += ReadingChanged;
                    filter.SetFilterAttrs(((SerialUSBIMU)sensor).GetFilterCutoff(), ((SerialUSBIMU)sensor).GetFilterBeta());

                    LogManager.LogInformation("{0} initialised as a {1}. Baud rate set to {2}", this.ToString(), sensorFamily.ToString(), ((SerialUSBIMU)sensor).GetInterval());
                    break;
            }

            StartListening(sensorFamily);
        }

        public void StartListening(SensorFamily sensorFamily)
        {
            switch (sensorFamily)
            {
                case SensorFamily.WindowsDevicesSensors:
                    ((Accelerometer)sensor).ReadingChanged += ReadingChanged;
                    break;
                case SensorFamily.SerialUSBIMU:
                    ((SerialUSBIMU)sensor).ReadingChanged += ReadingChanged;
                    break;
            }
        }

        public void StopListening(SensorFamily sensorFamily)
        {
            if (sensor is null)
                return;

            switch (sensorFamily)
            {
                case SensorFamily.WindowsDevicesSensors:
                    ((Accelerometer)sensor).ReadingChanged -= ReadingChanged;
                    break;
                case SensorFamily.SerialUSBIMU:
                    ((SerialUSBIMU)sensor).ReadingChanged -= ReadingChanged;
                    break;
            }
        }

        private void ReadingChanged(Vector3 AccelerationG, Vector3 AngularVelocityDeg)
        {
            this.reading.X = this.reading_fixed.X = (float)filter.axis1Filter.Filter(AccelerationG.X, XInputController.DeltaSeconds);
            this.reading.Y = this.reading_fixed.Y = (float)filter.axis2Filter.Filter(AccelerationG.Y, XInputController.DeltaSeconds);
            this.reading.Z = this.reading_fixed.Z = (float)filter.axis3Filter.Filter(AccelerationG.Z, XInputController.DeltaSeconds);

            base.ReadingChanged();
        }

        private void ReadingChanged(Accelerometer sender, AccelerometerReadingChangedEventArgs args)
        {
            foreach (char axis in reading_axis.Keys)
            {
                switch (ControllerService.handheldDevice.AccelerationAxisSwap[axis])
                {
                    default:
                    case 'X':
                        reading_axis[axis] = args.Reading.AccelerationX;
                        break;
                    case 'Y':
                        reading_axis[axis] = args.Reading.AccelerationY;
                        break;
                    case 'Z':
                        reading_axis[axis] = args.Reading.AccelerationZ;
                        break;
                }
            }

            this.reading.X = this.reading_fixed.X = (float)reading_axis['X'] * ControllerService.handheldDevice.AccelerationAxis.X;
            this.reading.Y = this.reading_fixed.Y = (float)reading_axis['Y'] * ControllerService.handheldDevice.AccelerationAxis.Y;
            this.reading.Z = this.reading_fixed.Z = (float)reading_axis['Z'] * ControllerService.handheldDevice.AccelerationAxis.Z;

            base.ReadingChanged();
        }

        public new Vector3 GetCurrentReading(bool center = false)
        {
            Vector3 reading = new Vector3()
            {
                X = center ? this.reading_fixed.X : this.reading.X,
                Y = center ? this.reading_fixed.Y : this.reading.Y,
                Z = center ? this.reading_fixed.Z : this.reading.Z
            };

            var readingZ = ControllerService.currentProfile.steering == 0 ? reading.Z : reading.Y;
            var readingY = ControllerService.currentProfile.steering == 0 ? reading.Y : -reading.Z;
            var readingX = ControllerService.currentProfile.steering == 0 ? reading.X : reading.X;

            if (ControllerService.currentProfile.inverthorizontal)
            {
                readingY *= -1.0f;
                readingZ *= -1.0f;
            }

            if (ControllerService.currentProfile.invertvertical)
            {
                readingY *= -1.0f;
                readingX *= -1.0f;
            }

            reading.X = readingX;
            reading.Y = readingY;
            reading.Z = readingZ;

            // Calculate angles around Y and X axis (Theta and Psi) using all 3 directions of accelerometer
            // Based on: https://www.digikey.com/en/articles/using-an-accelerometer-for-inclination-sensing               
            double angle_x_psi = -1 * (Math.Atan(reading.Y / (Math.Sqrt(Math.Pow(reading.X, 2) + Math.Pow(reading.Z, 2))))) * 180 / Math.PI;
            double angle_y_theta = -1 * (Math.Atan(reading.X / (Math.Sqrt(Math.Pow(reading.Y, 2) + Math.Pow(reading.Z, 2))))) * 180 / Math.PI;

            reading.X = (float)(angle_x_psi);
            reading.Y = (float)(angle_y_theta);

            return reading;
        }
    }
}