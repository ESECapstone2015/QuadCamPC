using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.IO;

using FTD2XX_NET;

namespace QuadCamPC {

    class Reader {
        static FTDI FtdiDevice;
        public static bool panic;

        public Reader(ref FTDI FtdiDevice) {
            Reader.FtdiDevice = FtdiDevice;
            Reader.panic = true;
        }

        public void read() {

            UInt32 numBytesAvailable = 0;
            byte[] readData = new byte[4096];// = {0};
            UInt32 nBytesRead = 0;
            FTDI.FT_STATUS ftStatus = FTDI.FT_STATUS.FT_OK;


            while ( true ) {

                while ( Reader.panic ) {
                    Thread.Sleep(1000);
                    //if ( ! Program.need_init ) {
                    //    Reader.panic = false;
                    //}
                }

                // Read any available bytes from USB
                ftStatus = Reader.FtdiDevice.GetRxBytesAvailable(ref numBytesAvailable);
                if ( ftStatus != FTDI.FT_STATUS.FT_OK ) {
                    Console.WriteLine("Failed to get number of bytes available to read (error " + ftStatus.ToString() + ")");
                    Reader.panic = true;
                    continue;
                }
                if ( numBytesAvailable > 0 ) {
                    Program.Read(Reader.FtdiDevice, numBytesAvailable, ref readData, ref nBytesRead);
                }
                else {
                    Thread.Sleep(10);
                }

            }
        }
    }
}