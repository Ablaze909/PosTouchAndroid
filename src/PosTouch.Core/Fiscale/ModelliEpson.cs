using System;
using PosTouch.Core.Diagnostica;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PosTouch.Core.Fiscale
{
    public class EpsonResponse
    {
        public bool Success { get; set; }
        public bool IsError { get; set; }
        public int Header1 { get; set; }
        public int Header2 { get; set; }
        public string Data { get; set; } = "";
        public int ErrorCode { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string RawHex { get; set; } = "";
    }
}
