using System;
using System.Diagnostics;
using System.Linq;
using Windows.Devices.I2c;

namespace i2c.ADS1015
{
    //
    // This implementation is based on Maxim sample implementation and has parts from ported C code
    // source: https://www.elfa.se/Web/Downloads/_t/ds/ads1015_eng_tds.pdf?mime=application%2Fpdf
    //and
    // https://learn.adafruit.com/adafruit-4-channel-adc-breakouts/programming
    //

    public class ADS1015 : IDisposable
    {
        readonly I2cDevice _i2cDevice;
        private UInt16 m_gainConfigFactor, m_conversionDelay;
        /*=========================================================================
    CONVERSION DELAY (in mS)
    -----------------------------------------------------------------------*/
        const byte ADS1015_CONVERSIONDELAY = 1;
        const byte ADS1115_CONVERSIONDELAY = 8;

        // Enable leaky abstraction
        // public I2cDevice I2CDevice { get { return _i2cDevice; } }

        public ADS1015(I2cDevice i2cDevice)
        {
            _i2cDevice = i2cDevice;
            m_gainConfigFactor = ADS1015_CONFIG_REGISTERS.PGA_4_096V; //GAIN_TWOTHIRDS = ADS1015_REG_CONFIG_PGA_6_144V
            m_conversionDelay = ADS1015_CONVERSIONDELAY;
        }
       
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_i2cDevice != null)
                    _i2cDevice.Dispose();
            }
        }


        private Int16 convertToMilliVolts(Int16 regConversationValue)
        {
            // GAIN_TWOTHIRDS) // 2/3x gain +/- 6.144V  1 bit = 3mV (default)
            // GAIN_ONE);      // 1x gain   +/- 4.096V  1 bit = 2mV
            // GAIN_TWO);      // 2x gain   +/- 2.048V  1 bit = 1mV
            // GAIN_FOUR);     // 4x gain   +/- 1.024V  1 bit = 0.5mV
            // GAIN_EIGHT);    // 8x gain   +/- 0.512V  1 bit = 0.25mV
            // GAIN_SIXTEEN);  // 16x gain  +/- 0.256V  1 bit = 0.125mV
            int programmableGain_Scaler = 0;
            switch (this.m_gainConfigFactor)
            {
                case ADS1015.ADS1015_CONFIG_REGISTERS.PGA_6_144V:
                    programmableGain_Scaler = 6144;
                    break;
                case ADS1015.ADS1015_CONFIG_REGISTERS.PGA_4_096V:
                    programmableGain_Scaler = 4096;
                    break;
                case ADS1015.ADS1015_CONFIG_REGISTERS.PGA_2_048V:
                    programmableGain_Scaler = 2048;
                    break;

                case ADS1015.ADS1015_CONFIG_REGISTERS.PGA_1_024V:
                    programmableGain_Scaler = 1024;
                    break;
                case ADS1015.ADS1015_CONFIG_REGISTERS.PGA_0_512V:
                    programmableGain_Scaler = 512;
                    break;
                case ADS1015.ADS1015_CONFIG_REGISTERS.PGA_0_256V:
                    programmableGain_Scaler = 256;
                    break;

                    /*    

                public const UInt16 PGA_4_096V = 0x0200;  // +/-4.096V range = Gain 1
                public const UInt16 PGA_2_048V = 0x0400;  // +/-2.048V range = Gain 2=default)
                public const UInt16 PGA_1_024V = 0x0600;  // +/-1.024V range = Gain 4
                public const UInt16 PGA_0_512V = 0x0800;  // +/-0.512V range = Gain 8
                public const UInt16 PGA_0_256V = 0x0A00;  // +/-0.256V range = Gain 16
                */
            }

            //ushort[] programmableGain_Scaler = { 6144, 4096, 2048, 1024, 512, 256 };
            return Convert.ToInt16((Int16)( regConversationValue * programmableGain_Scaler / 2048));

        }
        
        /// <summary>
        /// Convert to signed inteter. (diferential meassurements) can be negative
        /// ie FFFF = -1 etc.
        /// Note, must be 16bit fullrange. ADS1015 needs to be extended to 16bit
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private Int16 TwosComplementToSigned(UInt16 value)
        {
            //0xF98C == -1652
            if ((UInt16)(value & 0x8000) == 0x0000)
                return (Int16)value; //not negative,
            UInt16 tv = (UInt16)(value & 0x7FFF);
            UInt16 max = 0x7FFF;

            return (Int16)(tv - max - 1);

        }

        /**************************************************************************/
        /*!
            @brief  Writes 16-bits to the specified destination register
        */
        /**************************************************************************/
        public void writeRegister(byte registerAddress, UInt16 value)
        {
            // point to register address byte reg
            byte b1 = (byte)((value >> 8) & 0xFF);
            byte b2 = (byte)(value & 0xFF);
            _i2cDevice.Write(new byte[] { registerAddress, b1, b2 });
            

        }

        /**************************************************************************/
        /*!
            @brief  Writes 16-bits to the specified destination register
        */
        /**************************************************************************/
        public UInt16 readRegister(byte registerAddress)
        {
            //_i2cDevice.Write(new byte[] { ADS1015_POINTER_REGISTERS.ADS1015_REG_POINTER_CONFIG });
            var i2cread = new byte[2];
            _i2cDevice.WriteRead(new byte[] { registerAddress }, i2cread);
            UInt16 val = (UInt16)( i2cread[0] << 8 | i2cread[1]);
            return val;

            
        }

        private void waitforconversationtoFinish()
        {
            UInt16 regval = 0;
            do
            {
                System.Threading.Tasks.Task.Delay(5 + m_conversionDelay).Wait();
                regval = this.readRegister(ADS1015.ADS1015_POINTER_REGISTERS.POINTER_CONFIG);
            } while ((regval & 0x8000) == 0);
        }


        public enum SINGLE_ADC_PORTS { Port_0, Port_1, Port_2, Port_3}

        /// <summary>
        /// Read single-ended, once shot meassurement
        /// </summary>
        /// <param name="channel"></param>
        /// <returns>Meassured value in milli Volts [mV]</returns>        
        public Int16 readADC_SingleEnded(SINGLE_ADC_PORTS channel)
        {
            if ((int)channel > 3) throw new InvalidOperationException("Bad channel input to function");


            // Start with default values
            UInt16 config = ADS1015_CONFIG_REGISTERS.CQUE_NONE | // Disable the comparator (default val)
                              ADS1015_CONFIG_REGISTERS.CLAT_NONLAT | // Non-latching (default val)
                              ADS1015_CONFIG_REGISTERS.CPOL_ACTVLOW | // Alert/Rdy active low   (default val)
                              ADS1015_CONFIG_REGISTERS.CMODE_TRAD | // Traditional comparator (default val)
                              ADS1015_CONFIG_REGISTERS.DR_1600SPS | // 1600 samples per second (default)
                              ADS1015_CONFIG_REGISTERS.MODE_SINGLE;   // Single-shot mode (default)

            // Set PGA/voltage range
            config |= m_gainConfigFactor;

            // Set single-ended input channel
            switch (channel)
            {
                case (SINGLE_ADC_PORTS.Port_0):
                    config |= ADS1015_CONFIG_REGISTERS.MUX_SINGLE_0;
                    break;
                case (SINGLE_ADC_PORTS.Port_1):
                    config |= ADS1015_CONFIG_REGISTERS.MUX_SINGLE_1;
                    break;
                case (SINGLE_ADC_PORTS.Port_2):
                    config |= ADS1015_CONFIG_REGISTERS.MUX_SINGLE_2;
                    break;
                case (SINGLE_ADC_PORTS.Port_3):
                    config |= ADS1015_CONFIG_REGISTERS.MUX_SINGLE_3;
                    break;
            }

            // Set 'start single-conversion' bit
            config |= ADS1015_CONFIG_REGISTERS.OS_SINGLE;

            // Write config register to the ADC
            this.writeRegister(ADS1015_POINTER_REGISTERS.POINTER_CONFIG, config);

            // Wait for the conversion to complete
            waitforconversationtoFinish();


            // Read the conversion results
            // Shift 12-bit results right 4 bits for the ADS1015
            UInt16 conversionRegVal = (UInt16)(this.readRegister(ADS1015_POINTER_REGISTERS.POINTER_CONVERSION) >> 4); /* m_bitShift;*/
            return convertToMilliVolts((Int16)conversionRegVal);
        }


        public enum DIFF_ADC_PORTS { Port_01, Port_23};
               
        /// <summary>
        /// This function sets up ADS1015 to diferential mode (one shot) 
        /// Measuring the voltage difference between the P(AIN0) and N(AIN1) input or AIN2 and AIN3
        /// Generates a signed value since the difference can be either positive or negative.
        /// </summary>
        /// <param name="channel"></param>
        /// <returns>Meassured value in milli Volts [mV]</returns>
        public Int16 readADC_Differential(DIFF_ADC_PORTS channel)
        {
            if (channel != DIFF_ADC_PORTS.Port_01 && channel != DIFF_ADC_PORTS.Port_23) throw new InvalidOperationException("Bad channel input to function");
            // Start with default values
            UInt16 config = ADS1015_CONFIG_REGISTERS.CQUE_NONE | // Disable the comparator (default val)
                              ADS1015_CONFIG_REGISTERS.CLAT_NONLAT | // Non-latching (default val)
                              ADS1015_CONFIG_REGISTERS.CPOL_ACTVLOW | // Alert/Rdy active low   (default val)
                              ADS1015_CONFIG_REGISTERS.CMODE_TRAD | // Traditional comparator (default val)
                              ADS1015_CONFIG_REGISTERS.DR_1600SPS | // 1600 samples per second (default)
                              ADS1015_CONFIG_REGISTERS.MODE_SINGLE;   // Single-shot mode (default)


            // Set PGA/voltage range
            config |= m_gainConfigFactor;

            // Set channels
            if(channel == DIFF_ADC_PORTS.Port_01)
                config |= ADS1015_CONFIG_REGISTERS.MUX_DIFF_0_1;          // AIN0 = P, AIN1 = N
            if (channel == DIFF_ADC_PORTS.Port_23)
                config |= ADS1015_CONFIG_REGISTERS.MUX_DIFF_2_3;          // AIN0 = P, AIN1 = N

            // Set 'start single-conversion' bit
            config |= ADS1015_CONFIG_REGISTERS.OS_SINGLE;

            // Write config register to the ADC
            writeRegister(ADS1015_POINTER_REGISTERS.POINTER_CONFIG, config);

            // Wait for the conversion to complete
            waitforconversationtoFinish();

            // Shift 12-bit results right 4 bits for the ADS1015
            UInt16 conversionRegVal = (UInt16)(this.readRegister(ADS1015_POINTER_REGISTERS.POINTER_CONVERSION) >> 4); /* m_bitShift;*/
            

                // Shift 12-bit results right 4 bits for the ADS1015,
                // making sure we keep the sign bit intact
            if (conversionRegVal > 0x07FF)
            {
                // negative number - extend the sign to 16th bit
                conversionRegVal |= 0xF000;
             }
            
            return convertToMilliVolts(TwosComplementToSigned(conversionRegVal));
        }


        /*    CONFIG REGISTER
        -----------------------------------------------------------------------*/

        private class ADS1015_CONFIG_REGISTERS
        {

            public const UInt16 OS_MASK = 0x8000;
            public const UInt16 OS_SINGLE = 0x8000;  // Write: Set to start a single-conversion
            public const UInt16 OS_BUSY = 0x0000;  // Read: Bit = 0 when conversion is in progress
            public const UInt16 OS_NOTBUSY = 0x8000;  // Read: Bit = 1 when device is not performing a conversion

            public const UInt16 MUX_MASK = 0x7000;
            public const UInt16 MUX_DIFF_0_1 = 0x0000;  // Differential P = AIN0, N = AIN1=default)
            public const UInt16 MUX_DIFF_0_3 = 0x1000;  // Differential P = AIN0, N = AIN3
            public const UInt16 MUX_DIFF_1_3 = 0x2000;  // Differential P = AIN1, N = AIN3
            public const UInt16 MUX_DIFF_2_3 = 0x3000;  // Differential P = AIN2, N = AIN3
            public const UInt16 MUX_SINGLE_0 = 0x4000;  // Single-ended AIN0
            public const UInt16 MUX_SINGLE_1 = 0x5000;  // Single-ended AIN1
            public const UInt16 MUX_SINGLE_2 = 0x6000;  // Single-ended AIN2
            public const UInt16 MUX_SINGLE_3 = 0x7000;  // Single-ended AIN3

            public const UInt16 PGA_MASK = 0x0E00;
            public const UInt16 PGA_6_144V = 0x0000;  // +/-6.144V range = Gain 2/3
            public const UInt16 PGA_4_096V = 0x0200;  // +/-4.096V range = Gain 1
            public const UInt16 PGA_2_048V = 0x0400;  // +/-2.048V range = Gain 2=default)
            public const UInt16 PGA_1_024V = 0x0600;  // +/-1.024V range = Gain 4
            public const UInt16 PGA_0_512V = 0x0800;  // +/-0.512V range = Gain 8
            public const UInt16 PGA_0_256V = 0x0A00;  // +/-0.256V range = Gain 16

            public const UInt16 MODE_MASK = 0x0100;
            public const UInt16 MODE_CONTIN = 0x0000;  // Continuous conversion mode
            public const UInt16 MODE_SINGLE = 0x0100;  // Power-down single-shot mode(default)

            public const UInt16 DR_MASK = 0x00E0;
            public const UInt16 DR_128SPS = 0x0000;  // 128 samples per second
            public const UInt16 DR_250SPS = 0x0020;  // 250 samples per second
            public const UInt16 DR_490SPS = 0x0040;  // 490 samples per second
            public const UInt16 DR_920SPS = 0x0060;  // 920 samples per second
            public const UInt16 DR_1600SPS = 0x0080;  // 1600 samples per second (default)
            public const UInt16 DR_2400SPS = 0x00A0;  // 2400 samples per second
            public const UInt16 DR_3300SPS = 0x00C0;  // 3300 samples per second

            public const UInt16 CMODE_MASK = 0x0010;
            public const UInt16 CMODE_TRAD = 0x0000;  // Traditional comparator with hysteresis(default)
            public const UInt16 CMODE_WINDOW = 0x0010;  // Window comparator

            public const UInt16 CPOL_MASK = 0x0008;
            public const UInt16 CPOL_ACTVLOW = 0x0000;  // ALERT/RDY pin is low when active=default;
            public const UInt16 CPOL_ACTVHI = 0x0008;  // ALERT/RDY pin is high when active

            public const UInt16 CLAT_MASK = 0x0004;  // Determines if ALERT/RDY pin latches once asserted
            public const UInt16 CLAT_NONLAT = 0x0000;  // Non-latching comparator=default;
            public const UInt16 CLAT_LATCH = 0x0004;  // Latching comparator

            public const UInt16 CQUE_MASK = 0x0003;
            public const UInt16 CQUE_1CONV = 0x0000;  // Assert ALERT/RDY after one conversions
            public const UInt16 CQUE_2CONV = 0x0001;  // Assert ALERT/RDY after two conversions
            public const UInt16 CQUE_4CONV = 0x0002;  // Assert ALERT/RDY after four conversions
            public const UInt16 CQUE_NONE = 0x0003;  // Disable the comparator and put ALERT/RDY in high state (default;
}
            /*=========================================================================*/


            /*    POINTER REGISTER
            -----------------------------------------------------------------------*/
        private class ADS1015_POINTER_REGISTERS
        {
            public const byte POINTER_MASK = 0x03;
            public const byte POINTER_CONVERSION = 0x00;
            public const byte POINTER_CONFIG = 0x01;
            public const byte POINTER_LOWTHRESH = 0x02;
            public const byte POINTER_HITHRESH = 0x03;
        }
    }
}
