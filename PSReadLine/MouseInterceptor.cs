/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.PowerShell
{
    public partial class PSConsoleReadLine
    {

        private const int WH_MOUSE_LL = 14;
        private const int STD_INPUT_HANDLE = -10;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private enum MouseMessages
        {
            WM_LBUTTONDOWN = 0x0201,
            WM_LBUTTONUP = 0x0202,
            WM_MOUSEMOVE = 0x0200,
            WM_MOUSEWHEEL = 0x020A,
            WM_RBUTTONDOWN = 0x0204,
            WM_RBUTTONUP = 0x0205
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        private enum ConsoleInputModes : uint
        {
            ENABLE_PROCESSED_INPUT = 0x0001,
            ENABLE_LINE_INPUT = 0x0002,
            ENABLE_ECHO_INPUT = 0x0004,
            ENABLE_WINDOW_INPUT = 0x0008,
            ENABLE_MOUSE_INPUT = 0x0010,
            ENABLE_INSERT_MODE = 0x0020,
            ENABLE_QUICK_EDIT_MODE = 0x0040,
            ENABLE_EXTENDED_FLAGS = 0x0080,
            ENABLE_AUTO_POSITION = 0x0100,
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);


        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);


        private static IntPtr hookId = IntPtr.Zero;
        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr WindowFromPoint(POINT p);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        private static HookProc hookProc = HookCallback;
        private static IntPtr ourHwnd;


        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr Hwnd;
            public uint Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public POINT Point;
        }

        const uint PM_NOREMOVE = 0;
        const uint PM_REMOVE = 1;

        const uint WM_QUIT = 0x0012;

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG lpMsg, IntPtr hwnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        private static extern bool GetMessage(out MSG lpMsg, IntPtr hwnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);


        private void MouseHookThreadProc()
        {
            ourHwnd = Process.GetCurrentProcess().MainWindowHandle;

            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                hookId = SetWindowsHookEx(WH_MOUSE_LL, hookProc, GetModuleHandle(curModule.ModuleName), 0);
            }


            // This needs to be improved to avoid being a busy-waiting loop
            MSG msg;
            while (true)
            {
                if (PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                // 2ms wait to reduce busy-waiting
                if (_singleton._closingWaitHandle.WaitOne(2))
                {
                    break;
                }
            }

            UnhookWindowsHookEx(hookId);
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && MouseMessages.WM_RBUTTONDOWN == (MouseMessages)wParam)
            {
                MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));

                if (OurWindowClicked(hookStruct.pt) &&
                    ClickedInsideClientArea(hookStruct.pt) &&
                    InQuickEditMode())
                {
                    PSConsoleReadLine.Paste();
                    return (IntPtr)1; // Suppress this event
                }
            }

            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        private static bool OurWindowClicked(POINT clickPoint)
        {
            // Checks if mouse clicked occured on our window
            if (WindowFromPoint(clickPoint) == ourHwnd)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private static bool ClickedInsideClientArea(POINT clickPoint)
        {
            // Far from a correct test! It doesn't account for scrollbars and possibly window
            // borders on the bottom. Looking for positive coords seems to work for avoiding
            // top and left window borders

            bool conversionResult = ScreenToClient(ourHwnd, ref clickPoint);
            if (conversionResult && clickPoint.x >= 0 && clickPoint.y >= 0)
                return true;
            else
                return false;
        }

        private static bool InQuickEditMode()
        {
            IntPtr consoleHandle = GetStdHandle(STD_INPUT_HANDLE);

            uint consoleMode;
            if (!GetConsoleMode(consoleHandle, out consoleMode))
            {
                return false;
            }
            else
            {
                if (((ConsoleInputModes)consoleMode).HasFlag(ConsoleInputModes.ENABLE_QUICK_EDIT_MODE))
                    return true;
                else
                    return false;
            }
        }
        

    }
}
