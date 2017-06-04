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
        static string iotHubUri = "AHPoolMonitorering.azure-devices.net";
        static string deviceKey = "C5WR75Lt0Q2Rip5912aa8jS1VJy2BCVTDN4PO+gFqJo=";

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

        //webIC dallas givare i2c.ROM_NO  = "286b4b6207000051"
        //Elfa givare "28f876100700007e"
        static  string ID_WaterTemperature = "28f876100700007e";
        static string ID_OutDoorAirTemperature = "286b4b6207000051";
        
        
        public struct TempSensorProp
        {
            public byte[] romId;
            public string sRomIdname;
            public int ix;
            public bool SensorDetected;
            public double temperature;
            public bool readingValid;
        }

        public struct myData
        {
            public double data;
            public int numberofMeasurements;
        }
        public class TelemetricData
        {
            public myData waterTemp;
            public myData OutdoorTemp;
            public myData ORP;
            public myData PH;
            public int dataCollections;
            public DateTime dtStart;
        }
        private TelemetricData myTelemetrics = new TelemetricData();

        public MainPage()
        {
            this.InitializeComponent();

            myTelemetrics.dtStart = DateTime.Now;

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(3000);
            timer.Tick += Timer_Tick;

            InitStuff();

            timer.Start();



        }

        private void InitStuff()
        {

            try
            {
                TbLog.Text += "init" + "\r\n";

                deviceClient = DeviceClient.Create(iotHubUri, new DeviceAuthenticationWithRegistrySymmetricKey("myFirstDevice", deviceKey), TransportType.Mqtt);

                InitGPIO();
                InitI2c_ADS1015_ADC();

                if (TEMPERATUREREAD_ENABLED)
                    InitI2C_DS248_100();
                if (myTempSensors[0].SensorDetected)
                    TBNote.Text = myTempSensors[0].sRomIdname;
                if (myTempSensors[1].SensorDetected)
                    TBNote_2.Text = myTempSensors[1].sRomIdname;


                TbLog.Text += "init done" + "\r\n";

                ProjectStatus.Text = "Water Quality application running";
            }
            catch (Exception ex)
            {
                TbLog.Text += "Init Error: " + ex.Message + "\r\n";
                Task.Delay(3000);
            }

        }

    


        private void Timer_Tick(object sender, object e)
        {
            DoProgram(); 
        }

        private void DoProgram()
        {

            try
            {
                double Ph, Orp, WaterTemp, OutDoorTemp;
                Ph = Orp = WaterTemp = OutDoorTemp = 0;

                GetWaterQuality(ref Orp, ref Ph);
                GetWaterLevelinput();
                GetTemperatures();

                //sort correct id out
                int WaterTempIx = 0;
                for (int i = 0; i < myTempSensors.Count(); i++)
                    if (myTempSensors[i].sRomIdname == ID_WaterTemperature)
                        WaterTempIx = i;
                int OutdoorTempIx = WaterTempIx==0?1:0;
                
                
                //get storedata to calc average
                if (myTempSensors[WaterTempIx].readingValid)
                {
                    myTelemetrics.waterTemp.data += myTempSensors[WaterTempIx].temperature;
                    myTelemetrics.waterTemp.numberofMeasurements++;
                }
                if (myTempSensors[OutdoorTempIx].readingValid)
                {
                    myTelemetrics.OutdoorTemp.data += myTempSensors[OutdoorTempIx].temperature;
                    myTelemetrics.OutdoorTemp.numberofMeasurements++;
                }
                myTelemetrics.ORP.data += Orp;
                myTelemetrics.ORP.numberofMeasurements++;
                myTelemetrics.PH.data += Ph;
                myTelemetrics.PH.numberofMeasurements++;
                myTelemetrics.dataCollections++;


                //Send data to cloud
                //TODO, use datetime
                /*DateTime dt = DateTime.Now();
                TimeSpan ts = DateTime.Now() - dt;
                ts.TotalSeconds*/

                if ((DateTime.Now - myTelemetrics.dtStart).TotalSeconds >= 60*5)
                {
                    WaterTemp = myTelemetrics.waterTemp.data / myTelemetrics.waterTemp.numberofMeasurements;
                    OutDoorTemp = myTelemetrics.OutdoorTemp.data / myTelemetrics.OutdoorTemp.numberofMeasurements;
                    Ph = myTelemetrics.PH.data / myTelemetrics.PH.numberofMeasurements;
                    Orp = myTelemetrics.ORP.data / myTelemetrics.ORP.numberofMeasurements;

                    //Send data to cloud
                    SendDeviceToCloudMessagesAsync(Ph, Orp, WaterTemp, OutDoorTemp);


                    //clear data
                    myTelemetrics = new TelemetricData();
                    myTelemetrics.dtStart = DateTime.Now;
                }

                //Flash LED + GUI... todo, move to its own timer func
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
                }


                //Task.Delay(3000);
            }
            catch( Exception ex)
            {
                TbLog.Text += "Error: " + ex.Message + "\r\n";
            }
          
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
           
                double temperature;
                if (TEMPERATUREREAD_ENABLED)
                {
                    
                    if (myTempSensors[1].SensorDetected)
                    {
                        ReadTemperature(ref DS2482_i2c, ref myTempSensors[1].romId, out temperature, out myTempSensors[1].readingValid);
                        myTempSensors[1].temperature = temperature;
                        if(myTempSensors[1].readingValid)
                            TBNote_2.Text = myTempSensors[1].sRomIdname + ": " + Convert.ToString(temperature) + " [c]";
                        else
                            TBNote_2.Text = myTempSensors[1].sRomIdname + ": Invalid";


                }

                    if (myTempSensors[0].SensorDetected)
                    {
                        ReadTemperature(ref DS2482_i2c, ref myTempSensors[0].romId, out temperature, out myTempSensors[0].readingValid);
                        myTempSensors[0].temperature = temperature;
                        if (myTempSensors[0].readingValid)
                            TBNote.Text = myTempSensors[0].sRomIdname + ": " + Convert.ToString(temperature) + " [c]";
                        else
                            TBNote.Text = myTempSensors[0].sRomIdname + ": Invalid";
                    }

                }
        }


        void ResetOneWireAndMatchDeviceRomAddress(ref DS2482_100 i2c, ref byte[] OneWireAddress, ref bool isPresent)
        {
            isPresent = i2c.OneWireReset();
                        
            i2c.OneWireWriteByte(0x55); //RomCommand.MATCH
            foreach (var item in OneWireAddress)
            {
                i2c.OneWireWriteByte(item);
            }
        }
        void ReadTemperature(ref DS2482_100 i2c, ref byte[] romid, out double temp, out bool temperaturReadValid)
        {
            bool isPresent = false;
            temp = -999;
            
            //byte[] romid = i2c.ROM_NO;
            ResetOneWireAndMatchDeviceRomAddress(ref i2c, ref romid, ref isPresent);
            if (isPresent)
            {
                i2c.OneWireWriteByte(0x44); //Convert_T
                //i2c.EnableStrongPullup();
                System.Threading.Tasks.Task.Delay(200).Wait();
                ResetOneWireAndMatchDeviceRomAddress(ref i2c, ref romid, ref isPresent);
            }
            if (isPresent)
            {
                //var scratchpad = ReadScratchpad();
                i2c.OneWireWriteByte(0xBE);
                var scratchpadData = new byte[9];
                for (int i = 0; i < scratchpadData.Length; i++)
                    scratchpadData[i] = i2c.OneWireReadByte();
                int b3 = ((int)(scratchpadData[1] << 8) + scratchpadData[0]);
                temp = (double)(b3) * 0.0625;
            }
            temperaturReadValid = isPresent;
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
            settings.BusSpeed = I2cBusSpeed.StandardMode;
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
            settings.BusSpeed = I2cBusSpeed.StandardMode;
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
                myTempSensors[RomIdIx].SensorDetected = true;

                //access and read next sensor that is hoocked up to the one wire net.
                bret = DS2482_i2c.OnoWireNext();
                RomIdIx++;
            }
            


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
                OutDoorTemp = T2Meas/*,
                Date_Time = DateTime.Now.ToLocalTime()*/
            };
            var messageString = JsonConvert.SerializeObject(telemetryDataPoint);
            var message = new Message(Encoding.ASCII.GetBytes(messageString));

            await deviceClient.SendEventAsync(message);
            
        }

    }
}
