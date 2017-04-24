using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using System.Text;

using Windows.Devices.Gpio;
using Windows.Devices.I2c;
using Rinsen.IoT.OneWire;
using i2c.ADS1015;


// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace AppAzureHubClient
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        static DeviceClient deviceClient;
       
        private DispatcherTimer timer;
        private const int LED_PIN = 21, WATER_LEVEL_IN_PIN = 6;
        private GpioPin LedPin, WaterLevelPin;
        private GpioPinValue pinValue;
        
        private SolidColorBrush redBrush = new SolidColorBrush(Windows.UI.Colors.Red);
        private SolidColorBrush grayBrush = new SolidColorBrush(Windows.UI.Colors.LightGray);

        private ADS1015 ADS1015_i2c = null;
        private I2cDevice Temp_I2C = null;
        private DS2482_100 DS2482_i2c = null;

        private TempSensorProp[] myTempSensors = new TempSensorProp[3];

        private bool TEMPERATUREREAD_ENABLED = true;

        public struct TempSensorProp
        {
            public byte[] romId;
            public string sRomIdname;
            public int ix;
            public bool detected;
            public double temperature;
        }

        public MainPage()
        {
            this.InitializeComponent();

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(3000);
            timer.Tick += Timer_Tick;

            InitStuff();

            timer.Start();

        }

        private void InitStuff()
        {
            TbLog.Text += "init" + "\r\n";

            privateKeys personalConfig = new privateKeys();

            deviceClient = DeviceClient.Create(personalConfig.iotHubUri, new DeviceAuthenticationWithRegistrySymmetricKey("myFirstDevice", personalConfig.deviceKey), TransportType.Mqtt);

            InitGPIO();
            InitI2c_ADS1015_ADC();
            
            if (TEMPERATUREREAD_ENABLED)
                InitI2C_DS248_100();

            TbLog.Text += "init done" + "\r\n";
                      
            ProjectStatus.Text = "Water Quality application running";
        }


        private void Timer_Tick(object sender, object e)
        {
            DoProgram(); 
        }

        private void DoProgram()
        {
          
                double Ph, Orp, WaterTemp, OutDoorTemp;
                Ph = Orp = WaterTemp = OutDoorTemp = 0;

                GetWaterQuality(ref Orp, ref Ph);
                GetWaterLevelinput();
                GetTemperatures();
                WaterTemp = myTempSensors[0].temperature;
                OutDoorTemp = myTempSensors[1].temperature;

                SendDeviceToCloudMessagesAsync(Ph, Orp, WaterTemp, OutDoorTemp);

                
                Task.Delay(3000);
            
          
        }


        private void GetWaterLevelinput()
        {
            GpioPinValue LevelerInp = WaterLevelPin.Read();

            if (LevelerInp == GpioPinValue.Low)
                Rectangle1.Fill = redBrush;
            else
                Rectangle1.Fill = new SolidColorBrush(Windows.UI.Colors.LightGreen);
            
        }
        private void GetWaterQuality(ref double ORP, ref double PH)
        {
            if (ADS1015_i2c == null) return;

            double v0 = ADS1015_i2c.readADC_SingleEnded(ADS1015.SINGLE_ADC_PORTS.Port_0);
            double v1 = ADS1015_i2c.readADC_SingleEnded(ADS1015.SINGLE_ADC_PORTS.Port_1);

            double Vin = (v0 / 1000) * 200;
            ORP = ((2.5 - (Vin / 200)) / 1.037) * 1000;

            TxtORP.Text = string.Format("ORP: {0} mV", ORP);

            Vin = (v1 / 1000) * 200;
            PH = 0.0178 * Vin - 1.889;
            TxtPH.Text = string.Format("PH: {0}", PH);

        }

        private void GetTemperatures()
        {
            if (pinValue == GpioPinValue.High)
            {
                pinValue = GpioPinValue.Low;
                LedPin.Write(pinValue);
                LED.Fill = redBrush;
            }
            else
            {
                pinValue = GpioPinValue.High;
                LedPin.Write(pinValue);
                LED.Fill = grayBrush;
                double temperature;
                if (TEMPERATUREREAD_ENABLED)
                {
                    if (myTempSensors[0].detected)
                    {
                        ReadTemperature(ref DS2482_i2c, ref myTempSensors[0].romId, out temperature);
                        myTempSensors[0].temperature = temperature;
                        TBNote.Text = myTempSensors[0].sRomIdname + ": " + Convert.ToString(temperature) + " [c]";
                        
                    }
                    if (myTempSensors[1].detected)
                    {
                        ReadTemperature(ref DS2482_i2c, ref myTempSensors[1].romId, out temperature);
                        TBNote_2.Text = myTempSensors[1].sRomIdname + ": " + Convert.ToString(temperature) + " [c]";
                        myTempSensors[1].temperature = temperature;
                        
                    }
                }
            }



        }


        void ResetOneWireAndMatchDeviceRomAddress(ref DS2482_100 i2c, ref byte[] OneWireAddress)
        {
            i2c.OneWireReset();
            //byte[] OneWireAddress = new byte[] { 0x28, 0xf8, 0x76, 0x10, 0x07, 0x00, 0x00, 0x7e };

            i2c.OneWireWriteByte(0x55); //RomCommand.MATCH

            foreach (var item in OneWireAddress)
            {
                i2c.OneWireWriteByte(item);
            }
        }
        void ReadTemperature(ref DS2482_100 i2c, ref byte[] romid, out double temp)
        {
            temp = -999;
            //byte bret = i2c.ReadStatus(true);

            //byte[] romid = i2c.ROM_NO;
            ResetOneWireAndMatchDeviceRomAddress(ref i2c, ref romid);
            i2c.OneWireWriteByte(0x44); //Convert_T
            i2c.EnableStrongPullup();
            System.Threading.Tasks.Task.Delay(1000).Wait();
            ResetOneWireAndMatchDeviceRomAddress(ref i2c, ref romid);

            //var scratchpad = ReadScratchpad();
            i2c.OneWireWriteByte(0xBE);
            var scratchpadData = new byte[9];
            for (int i = 0; i < scratchpadData.Length; i++)
                scratchpadData[i] = i2c.OneWireReadByte();
            int b3 = ((int)(scratchpadData[1] << 8) + scratchpadData[0]);
            temp = (double)(b3) * 0.0625;


        }


        private void InitGPIO()
        {
            var gpio = GpioController.GetDefault();

            // Show an error if there is no GPIO controller
            if (gpio == null)
            {
                LedPin = null;
                TbLog.Text += "There is no GPIO controller on this device." + "\r\n";
                return;
            }

            LedPin = gpio.OpenPin(LED_PIN);
            pinValue = GpioPinValue.High;
            LedPin.Write(pinValue);
            LedPin.SetDriveMode(GpioPinDriveMode.Output);

            WaterLevelPin = gpio.OpenPin(WATER_LEVEL_IN_PIN);

            TbLog.Text += "GPIO pin initialized correctly." + "\r\n";



        }

        private async void InitI2c_ADS1015_ADC()
        {
            var settings = new I2cConnectionSettings(0x4A);  //49 == VDD, 4A == SDA
            settings.BusSpeed = I2cBusSpeed.FastMode;
            var controller = await I2cController.GetDefaultAsync();
            I2cDevice ADC_I2C = controller.GetDevice(settings);    /* Create an I2cDevice with our selected bus controller and I2C settings */

            ADS1015_i2c = new ADS1015(ADC_I2C);

        }

        /// <summary>
        /// INIT of One wire hardware Dallas DS2482S-100 – I2C master device
        /// </summary>
        private async void InitI2C_DS248_100()
        {
            const byte ACCEL_I2C_ADDR = 0x1B;         /* 7-bit I2C address of the ADXL345 with SDO pulled low */
            TBNote.Text = "i2c searching..";
            var settings = new I2cConnectionSettings(ACCEL_I2C_ADDR);
            settings.BusSpeed = I2cBusSpeed.FastMode;
            var controller = await I2cController.GetDefaultAsync();
            Temp_I2C = controller.GetDevice(settings);    /* Create an I2cDevice with our selected bus controller and I2C settings */
            TBNote.Text = Temp_I2C.DeviceId;



            // REGISTER SELECTION CODE
            const byte StatusReg = 0xF0;
            const byte ReadDataReg = 0xE1;
            const byte ConfigurationReg = 0xC3;


            byte[] ReadBuf = new byte[1];

            //set read pointer

            // device (DS2482-100) reset
            Temp_I2C.Write(new byte[] { 0xF0 });

            //again
            Temp_I2C.WriteRead(new byte[] { 0xE1, ReadDataReg }, ReadBuf);
            Temp_I2C.WriteRead(new byte[] { 0xE1, ConfigurationReg }, ReadBuf);
            Temp_I2C.WriteRead(new byte[] { 0xE1, StatusReg }, ReadBuf);



            DS2482_i2c = new DS2482_100(Temp_I2C);

            //Get ROM ID
            int RomIdIx = 0;
            bool bret = DS2482_i2c.OneWireFirst();
            while (bret == true && myTempSensors.GetUpperBound(0) > RomIdIx)
            {
                myTempSensors[RomIdIx].romId = new byte[8];
                //myTempSensors[RomIdIx].romId = DS2482_i2c.ROM_NO;
                int bix = 0;
                foreach (byte b in DS2482_i2c.ROM_NO)
                {
                    myTempSensors[RomIdIx].sRomIdname += string.Format("{0:x2}", b);
                    myTempSensors[RomIdIx].romId[bix++] = b;
                }


                myTempSensors[RomIdIx].ix = RomIdIx;
                myTempSensors[RomIdIx].detected = true;

                //access and read next sensor that is hoocked up to the one wire net.
                bret = DS2482_i2c.OnoWireNext();
                RomIdIx++;
            }

            TBNote.Text = myTempSensors[0].sRomIdname;
            TBNote_2.Text = myTempSensors[1].sRomIdname;


            byte bstat = DS2482_i2c.ReadStatus(true);
            if (DS2482_i2c.OneWireReset() == false)
                ;//expected response ==1 (present)

            //ReadTemperature(ref DS2482_i2c);


            //retry alternative 2

            /*
             var scratchpadData = new byte[9];
             // device (DS2482-100) reset
             I2C.Write(new byte[] { 0xF0 });
             System.Threading.Tasks.Task.Delay(20).Wait();
             bstat = DS2482_i2c.ReadStatus(true);
             DS2482_i2c.EnableStrongPullup();
             bstat = DS2482_i2c.ReadStatus(true);

             if (DS2482_i2c.OneWireReset() == false)
                 ;//expected response ==1 (present)
             DS2482_i2c.OneWireWriteByte(0xCC);
             DS2482_i2c.OneWireWriteByte(0xBE);

             scratchpadData = new byte[9];
             for (int i = 0; i < scratchpadData.Length; i++)
             {
                 scratchpadData[i] = DS2482_i2c.OneWireReadByte();
             }
             int b3 = ((int)(scratchpadData[1] << 8) + scratchpadData[0]);
             */





            return;

            /* Now that everything is initialized, create a timer so we read data every 100mS */
            //periodicTimer = new Timer(this.TimerCallback, null, 0, 100);
        }





        private static async void SendDeviceToCloudMessagesAsync(double PhMeas, double OrpMeas, double T1Meas, double T2Meas)
        {                        
            var telemetryDataPoint = new
            {
                deviceId = "myFirstDevice",
                //windSpeed = currentWindSpeed
                PH = PhMeas,
                ORP = OrpMeas,
                WaterTemp = T1Meas,
                OutDoorTemp = T2Meas               
            };
            var messageString = JsonConvert.SerializeObject(telemetryDataPoint);
            var message = new Message(Encoding.ASCII.GetBytes(messageString));

            await deviceClient.SendEventAsync(message);
            
        }

    }
}
