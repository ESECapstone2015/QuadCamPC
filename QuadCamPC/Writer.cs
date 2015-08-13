using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QuadCamPC {

    class Writer {

        static bool char_available;
        static char ch;

        public Writer() {
            Writer.char_available = false;
        }

        public bool CharAvailable() {
            return char_available;
        }

        public char GetChar() {
            char ch = Writer.ch;
            char_available = false;
            return ch;
        }

        public void write() {
            while ( true ) {
                while (char_available);
                Writer.ch = (char)Console.Read();
                char_available = true;
            }
        }
    }
}