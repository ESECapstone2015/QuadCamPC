using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;

using FTD2XX_NET;


namespace QuadCamPC {

    class Program {

        public static bool sync_mode = false;
        public static bool need_init = true;
        static FTDI.FT_STATUS ftStatus = FTDI.FT_STATUS.FT_OK;
        static FTDI ftdiDevice;

        // Set Async or Sync FIFO bit mode
        static void SetMode(bool sync_mode) {
            // Do nothing if already in desired mode (avoids unnecessary reset)
            if ( sync_mode == Program.sync_mode ) {
                //    return;
            }
            Program.sync_mode = sync_mode;
            // Reset to Async Mode
            ftStatus = ftdiDevice.SetBitMode(0xFF, FTDI.FT_BIT_MODES.FT_BIT_MODE_RESET);
            if ( ftStatus != FTDI.FT_STATUS.FT_OK ) {
                Console.WriteLine("Failed to reset device to AsyncFIFO mode (error " + ftStatus.ToString() + ")");
                return;
            }
            Thread.Sleep(10);
            if ( sync_mode ) {
                Thread.Sleep(100);
                ftStatus = ftdiDevice.SetBitMode(0xFF, FTDI.FT_BIT_MODES.FT_BIT_MODE_SYNC_FIFO);
                if ( ftStatus != FTDI.FT_STATUS.FT_OK ) {
                    Console.WriteLine("Failed to initialize SyncFIFO mode (error " + ftStatus.ToString() + ")");
                    return;
                }
                //Console.WriteLine("\n > Sync");
            }
            else {
                //Console.WriteLine("\n > Async");
            }
        }

        public static void SetAsyncMode() {
            SetMode(false);
        }

        public static void SetSyncMode() {
            SetMode(true);
        }

        static String GetQuadCamSerial(UInt32 ftdiDeviceCount) {

            // Allocate storage for device info list
            FTDI.FT_DEVICE_INFO_NODE[] ftdiDeviceList = new FTDI.FT_DEVICE_INFO_NODE[ftdiDeviceCount];

            // Populate our device list
            ftStatus = Program.ftdiDevice.GetDeviceList(ftdiDeviceList);

            if ( ftStatus == FTDI.FT_STATUS.FT_OK ) {
                for ( UInt32 i = 0; i < ftdiDeviceCount; i++ ) {
                    if ( ftdiDeviceList[i] != null ) {
                        if ( ftdiDeviceList[i].Description.ToString() == "QuadCam" ) {
                            return ftdiDeviceList[i].SerialNumber.ToString();
                        }
                    }
                }
            }
            return "";
        }


        // Wait until at least one QuadCam is attached
        // Returns the first QuadCam serial number found
        // Return "" after timeout_seconds or no timeout if timeout_seconds < 1
        static String WaitForQuadCam(int timeout_seconds) {
            int timeout_ticks = timeout_seconds * 10;
            int timeout_ticks_counter = 0;
            bool reported_fail = false;
            bool reported_no_quadcam = false;
            bool reported_no_ftdi = false;
            UInt32 ftdiDeviceCount = 0;

            // Poll devices until timeout expires
            while ( timeout_ticks_counter < timeout_ticks || timeout_seconds <= 0 ) {

                // Attempt to count number of attached devices
                ftStatus = ftdiDevice.GetNumberOfDevices(ref ftdiDeviceCount);

                // If device count was determined...
                if ( ftStatus == FTDI.FT_STATUS.FT_OK ) {

                    // If at least one FTDI device was detected...
                    if ( ftdiDeviceCount != 0 ) {
                        String serial = GetQuadCamSerial(ftdiDeviceCount);

                        // If a QuadCam serial number was found...
                        if ( serial != "" ) {
                            return serial;
                        }

                        // If no QuadCam serials found, report once and recheck indefinitely
                        else {
                            if ( !reported_no_quadcam ) {
                                Console.WriteLine("No QuadCam found");
                                reported_no_quadcam = true;
                            }
                        }
                    }

                    // If device count is 0, report the absence once and recheck indefinitely
                    else {
                        if ( !reported_no_ftdi ) {
                            Console.WriteLine("No FTDI devices detected");
                            reported_no_ftdi = true;
                        }
                    }
                }

                // If device count cannot be determined, report an error once and reattempt indefinitely
                else {
                    if ( !reported_fail ) {
                        Console.WriteLine("Failed to get number of devices (error " + ftStatus.ToString() + ")");
                        reported_fail = true;
                    }
                }

                Thread.Sleep(100);
                ++timeout_ticks_counter;
            }

            Console.WriteLine("Timeout: WaitForQuadCam(): No QuadCams detected");
            return "";
        }

        public static void Initialize() {
            String quadcam_serial;
            while ( true ) {
                quadcam_serial = WaitForQuadCam(0); // 0 = no timeout

                ftStatus = Program.ftdiDevice.OpenBySerialNumber(quadcam_serial);
                if ( ftStatus != FTDI.FT_STATUS.FT_OK ) {
                    continue;
                }
                break;
            }

            Console.WriteLine("Found QuadCam #" + quadcam_serial);

            SetAsyncMode();

            ftStatus = Program.ftdiDevice.SetLatency(2);
            // ftStatus = FtdiDevice.SetFlowControl(FTDI.FT_FLOW_CONTROL.FT_FLOW_RTS_CTS, 0, 0);
            ftStatus = Program.ftdiDevice.SetTimeouts(90, 200);
        }


        static bool is_frame = false;
        //static bool frame_end = false;
        const String frame_start_key = "\x89\xAB\xCD\xEF\x01\x23\x45\x67";
        const String frame_end_key = "\x01\x23\x45\x67\x89\xAB\xCD\xEF";
        const String sync_key = "SYNC MODE \n";
        static int frame_start_i = 0;
        static int frame_end_i = 0;
        static int sync_i = 0;

        static byte[] frame_raw = new byte[2 * 640 * 2048];
        static int frame_raw_i = 0;

        static byte[] frame_rgb565 = new byte[2 * 1280 * 1024];
        static int frame_rgb565_i = 0;

        static byte[] frame_rgb888 = new byte[3 * 1280 * 1024];
        static int frame_rgb888_i = 0;


        // Frame formats
        // up565 - unpacked RGB565: flat array, 2 bytes per pixel
        // p565  -   packed RGB565: 2D array, 1 int per pixel 
        // p888  -   packed RGB888: 2D array, 1 int per pixel 
        // up888 - unpacked RGB888: flat array, 3 bytes per pixel


        static UInt32 conv_565_to_888(UInt32 rgb565) {
            UInt32 rgb888;
            UInt32 r, g, b;
            r = (rgb565 >> 11) & 0x0000001F;
            g = (rgb565 >> 5) & 0x0000003F;
            b = (rgb565 >> 0) & 0x0000001F;

            r <<= 3;
            g <<= 2;
            b <<= 3;

            rgb888 = ((r << 16) & 0x00FF0000) | ((g << 8) & 0x0000FF00) | ((b << 0) & 0x000000FF);
            return rgb888;
        }

        static void up565_to_p565(byte[] up565, UInt32[,] p565) {
            int i = 0;
            int x, y;
            for ( y = 0; y < 1024; ++y ) {
                for ( x = 0; x < 1280; ++x ) {
                    p565[y, x] = (UInt32)((((UInt32)(up565[i+1]) << 8) & 0x0000FF00) | ((UInt32)(up565[i]) & 0x000000FF));
                    i += 2;
                }
            }
        }

        static void p565_to_p888(UInt32[,] p565, UInt32[,] p888) {
            int x, y;
            for ( y = 0; y < 1024; ++y ) {
                for ( x = 0; x < 1280; ++x ) {
                    p888[1023-y, x] = conv_565_to_888(p565[y, x]);
                }
                x = 0;
                //Console.Write("{0} Convert: 0x{1:X4} -> {2:X8}\n", y, p565[y, x], p888[y, x]);
            }
        }

        static void p888_to_up888(UInt32[,] p888, byte[] up888) {
            int i = 0;
            int x, y;
            for ( y = 0; y < 1024; ++y ) {
                for ( x = 0; x < 1280; ++x ) {
                    up888[i + 2] = (byte)((p888[y, x] >> 16) & 0x000000FF);
                    up888[i + 1] = (byte)((p888[y, x] >> 8) & 0x000000FF);
                    up888[i + 0] = (byte)((p888[y, x] >> 0) & 0x000000FF);
                    //up888[i + 0] = (byte)(0);
                    i += 3;
                }
            }
        }

        static void up565_to_up888(byte[] up565, byte[] up888) {

            UInt32[,] p565 = new UInt32[1024, 1280];
            UInt32[,] p888 = new UInt32[1024, 1280];

            up565_to_p565(up565, p565);
            p565_to_p888(p565, p888);
            p888_to_up888(p888, up888);

        }

        static void collate_frame(byte[] raw, byte[] up565) {
            int i = 0;
            int x, y;

            for ( y = 0; y < 1024; ++y ) {
                for ( x = 0; x < 640; ++x ) {
                    up565[i] = raw[y * 640 * 2 + x*2];
                    up565[i + 1] = raw[y * 640 * 2 + x*2 + 1];
                    i += 2;
                }
                for ( x = 0; x < 640; ++x ) {
                    up565[i] = raw[(y + 1024) * 640 * 2 + x*2];
                    up565[i + 1] = raw[(y + 1024) * 640 * 2 + x*2 + 1];
                    i += 2;
                }
            }
        }

        static bool check_key(char c, String key, ref int i) {
            if ( c == key[i] ) {
                ++i;
                if ( i == key.Length ) {
                    i = 0;
                    return true;
                }
            }
            else {
                i = 0;
            }
            return false;
        }

        static bool check_frame_start(char c) {
            return check_key(c, frame_start_key, ref frame_start_i);
        }

        static bool check_frame_end(char c) {
            return check_key(c, frame_end_key, ref frame_end_i);
        }

        static bool check_sync(char c) {
            return check_key(c, sync_key, ref sync_i);
        }

        // Crop full image into separate camera images
        // Utilizes AviSynth and MPC-HC media player to crop and render images
        static void crop_image(string script_dir)
        {
            // Find all script files within avisynth directory
            string[] scriptPaths = System.IO.Directory.GetFiles(script_dir, "crop*.avs");
            
            // Run each script using MPC-HC
            Process proc;
            foreach (string script in scriptPaths)
            {
                // Run MPC-HC in minimized mode, do not steal window focus, and close after rendering
                proc = System.Diagnostics.Process.Start(@"C:\Program Files (x86)\MPC-HC\mpc-hc.exe", script + @" /minimized /nofocus");
            }
        }

        static Stopwatch stopwatch = new Stopwatch();

        public static void Read(FTDI FtdiDevice, UInt32 numToRead, ref byte[] readData, ref UInt32 nBytesRead) {
            int i;
            bool frame_end = false;

            String line = "";

            UInt32 numBytesRead = 0;
            UInt32 numBytesAvailable = 0;

            while ( numBytesRead == 0 ) {

                ftStatus = Program.ftdiDevice.GetRxBytesAvailable(ref numBytesAvailable);
                if ( ftStatus != FTDI.FT_STATUS.FT_OK ) {
                    Console.WriteLine("Failed to get number of bytes available to read (error " + ftStatus.ToString() + ")");
                    Thread.Sleep(1000);
                    continue;
                }

                if ( numToRead == 0 ) {
                    ftStatus = Program.ftdiDevice.Read(readData, numBytesAvailable, ref numBytesRead);
                    Thread.Sleep(1);
                }
                else {
                    ftStatus = Program.ftdiDevice.Read(readData, numToRead, ref numBytesRead);
                }
                if ( ftStatus != FTDI.FT_STATUS.FT_OK ) {
                    Console.WriteLine("Failed to read data (error " + ftStatus.ToString() + ")");
                    Console.ReadKey();
                    return;
                }

            }

            nBytesRead = numBytesRead;
            numBytesRead = 0;


            if ( !Program.sync_mode ) {

                for ( i = 0; i < nBytesRead; ++i ) {

                    //line += String.Format("{0:X2}", readData[i]);
                    line += (char)readData[i];

                    if ( is_frame ) {
                        frame_raw[frame_raw_i] = readData[i];
                        ++frame_raw_i;
                    }

                    if ( check_frame_start((char)readData[i]) ) {
                        is_frame = true;
                        Console.Write("Frame Start\n"); 
                        stopwatch.Reset();
                        stopwatch.Start();
                    }

                    frame_end = check_frame_end((char)readData[i]);
                    if ( is_frame && frame_end ) {
                        stopwatch.Stop();
                        int frame_size = 2 * 1024 * 1280;
                        double transfer_dur = stopwatch.Elapsed.TotalSeconds;
                        Console.Write("Frame End - {0:F2} Mb/sec \n", (frame_size*8) / (1024 * 1024 * transfer_dur));
                        is_frame = false;
                        collate_frame(frame_raw, frame_rgb565);
                        frame_raw_i = 0;
                        up565_to_up888(frame_rgb565, frame_rgb888);

                        string pwd = System.IO.Directory.GetCurrentDirectory(); // Executable directory (ie. ./bin/Debug/)

                        // Write unpacked RGB565 format
                        System.IO.StreamWriter file1 = new System.IO.StreamWriter(pwd + @"\usb_up565.dat");
                        for ( i = 0; i < 2 * 1024 * 1280; ++i ) {
                            file1.Write(frame_rgb565[i]);
                        }
                        file1.Close();

                        // Write unpacked RGB888 format
                        System.IO.BinaryWriter file2 = new System.IO.BinaryWriter(new System.IO.FileStream(pwd + @"\usb_up888.dat", System.IO.FileMode.Create));
                        for ( i = 0; i < 3 * 1024 * 1280; ++i ) {
                            file2.Write (frame_rgb888[i]);
                        }
                        file2.Close();

                        Console.Write("Image Saved\n");

                        // Convert raw image data to bitmap, 1280x1024 res
                        string strCmdText;
                        strCmdText = "1280 1024 " + pwd + @"\usb_up888.dat " + pwd + @"\usb_up888.bmp";
                        System.Diagnostics.Process.Start(pwd + @"\..\..\dat_to_bmp.exe", strCmdText);

                        /*
                        // Display bitmap using MS Paint
                        System.Diagnostics.Process.Start(@"C:\windows\system32\mspaint.exe", pwd + @"\usb_up888.bmp");
                        */

                        // Crop bitmap into separate camera images
                        // (script directory is two levels back from pwd)
                        crop_image(pwd + @"\..\..\avisynth\");

                        Console.Write("Ready\n");

                        using (var client = new MailslotClient("QuadCam\\ImageReady"))
                        {
                            client.SendMessage("ImageReady");
                        }
                    }
                    /*
                    if ( check_sync((char)readData[i]) ) {
                        SetSyncMode();
                    }
                    */
                }

                if ( !is_frame && !frame_end ) {
                    Console.Write(line);
                }
                line = "";
            }
        }

        public static void Write(FTDI FtdiDevice, byte[] vals, UInt32 nWriteBytes) {
            FTDI.FT_STATUS ftStatus = FTDI.FT_STATUS.FT_OK;
            UInt32 nBytesWritten = 0;
            UInt32 byteCount = 0;
            //byte[] vals = { 0x1F };
            //byte[] vals = { val };
            //do
            //{
            // ftStatus = myFtdiDevice.Write((byte)(0xAA), 1, ref numBytesWritten);
            //sync_mode = false;

            SetAsyncMode();

            while ( nBytesWritten != nWriteBytes ) {
                ftStatus = FtdiDevice.Write(vals, nWriteBytes - nBytesWritten, ref byteCount);
                if ( ftStatus != FTDI.FT_STATUS.FT_OK ) {
                    // Wait for a key press
                    Console.WriteLine("Failed to write data (error " + ftStatus.ToString() + ")");
                    //Console.ReadKey();
                    //return;
                }
                nBytesWritten += byteCount;
                for ( int i = 0; i < nWriteBytes - nBytesWritten; ++i ) {
                    vals[i] = vals[i + byteCount];
                }
               // Console.WriteLine("Wrote {0} bytes ( {0} of {0} total )", byteCount, nBytesWritten, nWriteBytes);
                //if ( numBytesWritten != nBytes ) {
                //    Console.WriteLine("Failed to write data (num " + numBytesWritten.ToString() + ")");
                //}
                //}
                //while (Console.ReadKey().Key != ConsoleKey.Spacebar);

                //Console.WriteLine("\r\nWrote {0} byte, it was {1:X2}", numBytesWritten, vals[0]);
            }
            //sync_mode = true;
        }

        static int hexToAscii(int n) {
            if ( n >= 0 && n <= 9 ) {
                return (char)(n + (int)'0');
            }
            else {
                return (char)((n - 10) + (int)'A');
            }
        }

        static int asciiToHex(int c) {
            if ( c >= (int)'0' && c <= (int)'9' ) {
                return c - (int)'0';
            }
            else {
                if ( c >= (int)'a' && c <= (int)'f' ) {
                    return (c + 10) - (int)'a';
                }
                else {
                    if ( c >= (int)'A' && c <= (int)'F' ) {
                        return (c + 10) - (int)'A';
                    }
                }
            }
            return 0;
        }

        static void Main(string[] args) {
            bool isRunning;
            byte[] writeData = new byte[256];
            UInt32 nWriteBytes = 0;

            Console.WriteLine(System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + " v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
            Console.WriteLine("");
            Console.WriteLine("Cameras");
            Console.WriteLine("");
            Console.WriteLine("  a  Disable CC1");
            Console.WriteLine("  b  Disable CC2");
            Console.WriteLine("  c  Disable CC3");
            Console.WriteLine("  d  Disable CC4");
            Console.WriteLine("");
            Console.WriteLine("Controls");
            Console.WriteLine("");
            Console.WriteLine("  ^  Reset");
            Console.WriteLine("  +  Capture / Cancel");
            Console.WriteLine("  *  Instant Capture Mode ");
            Console.WriteLine("  /  Delayed Capture Mode   [Default]");
            Console.WriteLine("  >  Image ---> USB         [Default]");
            Console.WriteLine("  .  Image -/-> USB");
            Console.WriteLine("  ~  Auto Framerate         [Default]");
            Console.WriteLine("  !  Fixed Framerate ");
            Console.WriteLine("  ]  Video During Transfer  [Default]");
            Console.WriteLine("  [  Still During Transfer");
            Console.WriteLine("  }  Capture Sync          [Default]");
            Console.WriteLine("  {  Capture No Sync");
            Console.WriteLine("  #  Clear Video");
            Console.WriteLine("  ?  Print RAM errors");
            Console.WriteLine("");
            Console.WriteLine("Pages");
            Console.WriteLine("");
            Console.WriteLine("  0  Loading Page");
            Console.WriteLine("  1  Probe Page");
            Console.WriteLine("  2  Check Page");
            Console.WriteLine("  3  Test Page");
            Console.WriteLine("  4  Status Page");
            Console.WriteLine("  5  Console");
            Console.WriteLine("  6  Still Captures");
            Console.WriteLine("  7  Camera Video");
            Console.WriteLine("  8  Vertical Stripes");
            Console.WriteLine("  9  Screen Saver");
            Console.WriteLine("");

            // Create new instance of the FTDI device class
            Program.ftdiDevice = new FTDI();
            //ftdiDevice.SetTimeouts(6000, 6000);


            Reader reader = new Reader(ref Program.ftdiDevice);
            Thread readerThread = new Thread(new ThreadStart(reader.read));
            readerThread.Start();

            Writer writer = new Writer();
            Thread writerThread = new Thread(new ThreadStart(writer.write));
            writerThread.Start();


            writeData[0] = (byte)'\n';
            nWriteBytes = 1;
            
            isRunning = true;
            while ( isRunning ) {

                if ( Reader.panic ) {
                    need_init = true;
                    //Console.WriteLine("Panic");
                    //Thread.Sleep(1000);
                }

                if ( need_init ) {
                    Initialize();
                    need_init = false;
                    Reader.panic = false;
                }

                // Read text from console if available     
                while ( writer.CharAvailable() ) {
                    //if ( writer.CharAvailable() ) {
                    writeData[nWriteBytes] = (byte)writer.GetChar();
                    nWriteBytes += 1;
                    //}
                    Thread.Sleep(1);
                }
                //writeData[nWriteBytes] = (byte)'\n';
                //nWriteBytes += 1;

                // Write console bytes to USB
                if ( nWriteBytes > 0 ) {
                    //for ( i = 0; i < nWriteBytes; ++i ) {
                    //    Write(Program.ftdiDevice, (byte)writeData[i], );
                    //}
                    Write(Program.ftdiDevice, writeData, nWriteBytes);
                }
                else {
                    Thread.Sleep(10);
                }
                nWriteBytes = 0;
            }

            readerThread.Abort();

            // Close our device
            //ftStatus = myFtdiDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX);
            ftStatus = Program.ftdiDevice.Close();
            if ( ftStatus != FTDI.FT_STATUS.FT_OK ) {
                Console.WriteLine("Didn't close correctly");
            }

            // Wait for a key press
            Console.WriteLine("Press any key to continue.");
            Console.ReadKey();
            return;
        }
    }
}
