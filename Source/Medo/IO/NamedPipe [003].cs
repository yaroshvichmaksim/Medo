/* Josip Medved <jmedved@jmedved.com> * www.medo64.com * MIT License */

//2012-11-24: Suppressing bogus CA5122 warning (http://connect.microsoft.com/VisualStudio/feedback/details/729254/bogus-ca5122-warning-about-p-invoke-declarations-should-not-be-safe-critical); removing link demands.
//2010-01-25: Exposing handle via GetHandle method.
//2010-01-23: Initial version.


using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Permissions;
using System.Threading;

namespace Medo.IO {

    /// <summary>
    /// Communication via named pipes.
    /// </summary>
    public class NamedPipe : IDisposable {

        /// <summary>
        /// Create new instance.
        /// </summary>
        /// <param name="pipeName">Pip name.</param>
        public NamedPipe(string pipeName) {
            PipeName = pipeName;
        }

        /// <summary>
        /// Gets pipe name.
        /// </summary>
        public string PipeName { get; private set; }

        /// <summary>
        /// Gets full pipe name (e.g. \\.\pipe\example).
        /// </summary>
        public string FullPipeName { get { return @"\\.\pipe\" + PipeName; } }

        private NativeMethods.FileSafeHandle SafeHandle = null;

        /// <summary>
        /// Gets native handle.
        /// </summary>
        public IntPtr GetHandle() {
            return (SafeHandle != null) ? SafeHandle.DangerousGetHandle() : IntPtr.Zero;
        }


        /// <summary>
        /// Creates pipe.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Pipe is already open.</exception>
        /// <exception cref="System.IO.IOException">Cannot create named pipe.</exception>
        public void Create() {
            if (SafeHandle != null) { throw new InvalidOperationException("Pipe is already open."); }
            SafeHandle = NativeMethods.CreateNamedPipe(FullPipeName, NativeMethods.PIPE_ACCESS_DUPLEX, NativeMethods.PIPE_TYPE_BYTE | NativeMethods.PIPE_READMODE_BYTE | NativeMethods.PIPE_WAIT, NativeMethods.PIPE_UNLIMITED_INSTANCES, 4096, 4096, NativeMethods.NMPWAIT_USE_DEFAULT_WAIT, IntPtr.Zero);
            if (SafeHandle.IsInvalid) { throw new IOException("Cannot create named pipe.", new Win32Exception()); }
        }

        /// <summary>
        /// Creates pipe with full access rights for everyone.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Pipe is already open.</exception>
        /// <exception cref="System.IO.IOException">Cannot create named pipe.</exception>
        public void CreateWithFullAccess() {
            if (SafeHandle != null) { throw new InvalidOperationException("Pipe is already open."); }

            var sec = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent, null, null, null, null);
            var sa = new NativeMethods.SECURITY_ATTRIBUTES {
                nLength = Marshal.SizeOf(typeof(NativeMethods.SECURITY_ATTRIBUTES)),
                bInheritHandle = true
            };

            var secBinary = new byte[sec.BinaryLength];
            sec.GetBinaryForm(secBinary, 0);
            sa.lpSecurityDescriptor = Marshal.AllocHGlobal(secBinary.Length);
            Marshal.Copy(secBinary, 0, sa.lpSecurityDescriptor, secBinary.Length);

            SafeHandle = NativeMethods.CreateNamedPipe(FullPipeName, NativeMethods.PIPE_ACCESS_DUPLEX, NativeMethods.PIPE_TYPE_BYTE | NativeMethods.PIPE_READMODE_BYTE | NativeMethods.PIPE_WAIT, NativeMethods.PIPE_UNLIMITED_INSTANCES, 4096, 4096, NativeMethods.NMPWAIT_USE_DEFAULT_WAIT, ref sa);
            if (SafeHandle.IsInvalid) { throw new IOException("Cannot create named pipe.", new Win32Exception()); }
        }

        /// <summary>
        /// Opens existing named pipe.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Pipe is already open.</exception>
        /// <exception cref="System.IO.IOException">Cannot find open named pipe. -or- Cannot open named pipe.</exception>
        public void Open() {
            if (SafeHandle != null) { throw new InvalidOperationException("Pipe is already open."); }
            if (NativeMethods.WaitNamedPipe(FullPipeName, NativeMethods.NMPWAIT_USE_DEFAULT_WAIT) == false) {
                throw new IOException("Cannot find open named pipe.", new Win32Exception());
            }
            SafeHandle = NativeMethods.CreateFile(FullPipeName, NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE, 0, System.IntPtr.Zero, NativeMethods.OPEN_EXISTING, NativeMethods.FILE_ATTRIBUTE_NORMAL, System.IntPtr.Zero);
            if (SafeHandle.IsInvalid) { throw new IOException("Cannot open named pipe.", new Win32Exception()); }
        }

        /// <summary>
        /// Closes named pipe.
        /// </summary>
        public void Close() {
            if (SafeHandle != null) {
                SafeHandle.Close();
                SafeHandle = null;
            }
        }

        /// <summary>
        /// Connects named pipe and waits for incomming connections.
        /// </summary>
        /// <exception cref="System.IO.IOException">Cannot connect to pipe.</exception>
        public void Connect() {
            if (NativeMethods.ConnectNamedPipe(SafeHandle, IntPtr.Zero) == false) {
                throw new IOException("Cannot connect to pipe.", new Win32Exception());
            }
        }

        /// <summary>
        /// Disconnects named pipe. Notice that this function will not wait for client to read data.
        /// </summary>
        /// <exception cref="System.IO.IOException">Cannot disconnect pipe.</exception>
        public void Disconnect() {
            if (NativeMethods.DisconnectNamedPipe(SafeHandle) == false) {
                throw new IOException("Cannot disconnect pipe.", new Win32Exception());
            }
        }

        /// <summary>
        /// Returns only when client has read all currently outgoing bytes.
        /// </summary>
        /// <exception cref="System.IO.IOException">Cannot flush pipe.</exception>
        public void Flush() {
            if (NativeMethods.FlushFileBuffers(SafeHandle) == false) {
                throw new IOException("Cannot flush pipe.", new Win32Exception());
            }
        }

        /// <summary>
        /// Returns true if data is available for reading.
        /// </summary>
        public bool HasBytesToRead {
            get { return (BytesToRead > 0); }
        }

        /// <summary>
        /// Returns number of bytes waiting to be read.
        /// </summary>
        public int BytesToRead {
            get {
                if (SafeHandle == null) { return 0; }
                uint available = 0, bytesRead = 0, thismsg = 0;
                if (NativeMethods.PeekNamedPipe(SafeHandle, null, 0, ref bytesRead, ref available, ref thismsg)) {
                    return (int)available;
                } else {
                    return 0;
                }
            }
        }

        /// <summary>
        /// Returns bytes available in pipe.
        /// </summary>
        /// <exception cref="System.IO.IOException">Pipe is not open. -or- Cannot read from named pipe. -or- Not all bytes can be read.</exception>
        public byte[] ReadAvailable() {
            if (SafeHandle == null) { throw new InvalidOperationException("Pipe is not open."); }

            var available = BytesToRead;
            if (available == 0) { return new byte[] { }; }

            var buffer = new byte[available];
            uint read = 0;
            var overlapped = new NativeOverlapped();
            if (!NativeMethods.ReadFile(SafeHandle, buffer, (uint)buffer.Length, ref read, ref overlapped)) {
                throw new IOException("Cannot read from named pipe.", new Win32Exception());
            }
            if (read != available) {
                throw new IOException("Not all bytes can be read.");
            }
            return buffer;
        }

        /// <summary>
        /// Writes bytes to pipe.
        /// </summary>
        /// <param name="buffer">Buffer.</param>
        /// <exception cref="System.IO.IOException">Pipe is not open. -or- Cannot write to pipe. -or- Not all data is written to pipe.</exception>
        public void Write(byte[] buffer) {
            if (SafeHandle.Equals(IntPtr.Zero)) { throw new InvalidOperationException("Pipe is not open."); }

            uint written = 0;
            var overlapped = new NativeOverlapped();
            if (NativeMethods.WriteFile(SafeHandle, buffer, (uint)buffer.Length, ref written, ref overlapped)) {
                if (written != buffer.Length) {
                    throw new IOException("Not all data is written to pipe.", new Win32Exception());
                }
            } else {
                throw new IOException("Cannot write to pipe.", new Win32Exception());
            }
        }


        #region IDisposable

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        [EnvironmentPermissionAttribute(SecurityAction.Demand, Unrestricted = true)]
        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing) {
            Close();
        }

        #endregion


        private static class NativeMethods {
#pragma warning disable IDE0049 // Simplify Names

            public const UInt32 FILE_ATTRIBUTE_NORMAL = 0;
            public const UInt32 GENERIC_READ = 0x80000000;
            public const UInt32 GENERIC_WRITE = 0x40000000;
            public const Int32 INVALID_HANDLE_VALUE = -1;
            public const UInt32 NMPWAIT_USE_DEFAULT_WAIT = 0x00000000;
            public const UInt32 OPEN_EXISTING = 3;
            public const UInt32 PIPE_ACCESS_DUPLEX = 0x00000003;
            public const UInt32 PIPE_READMODE_BYTE = 0x00000000;
            public const UInt32 PIPE_TYPE_BYTE = 0x00000000;
            public const UInt32 PIPE_UNLIMITED_INSTANCES = 255;
            public const UInt32 PIPE_WAIT = 0x00000000;


            [StructLayoutAttribute(LayoutKind.Sequential)]
            public struct SECURITY_ATTRIBUTES : IDisposable {
                public Int32 nLength;
                public IntPtr lpSecurityDescriptor;
                [MarshalAsAttribute(UnmanagedType.Bool)]
                public Boolean bInheritHandle;

                public void Dispose() {
                    CloseHandle(lpSecurityDescriptor);
                    lpSecurityDescriptor = IntPtr.Zero;
                    GC.SuppressFinalize(this);
                }
            }


            public class FileSafeHandle : SafeHandle {
                private static readonly IntPtr minusOne = new IntPtr(INVALID_HANDLE_VALUE);


                public FileSafeHandle()
                    : base(minusOne, true) { }


                public override bool IsInvalid {
                    get { return (IsClosed) || (base.handle == minusOne); }
                }

                protected override bool ReleaseHandle() {
                    return CloseHandle(base.handle);
                }

                public override string ToString() {
                    return base.handle.ToString();
                }

            }


            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA5122:PInvokesShouldNotBeSafeCriticalFxCopRule", Justification = "Warning is bogus.")]
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern Boolean CloseHandle(IntPtr hObject);

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA5122:PInvokesShouldNotBeSafeCriticalFxCopRule", Justification = "Warning is bogus.")]
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern Boolean ConnectNamedPipe(FileSafeHandle hNamedPipe, IntPtr lpOverlapped);

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA5122:PInvokesShouldNotBeSafeCriticalFxCopRule", Justification = "Warning is bogus.")]
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern FileSafeHandle CreateFile(String lpFileName, UInt32 dwDesiredAccess, UInt32 dwShareMode, IntPtr lpSecurityAttributes, UInt32 dwCreationDisposition, UInt32 dwFlagsAndAttributes, IntPtr hTemplateFile);

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA5122:PInvokesShouldNotBeSafeCriticalFxCopRule", Justification = "Warning is bogus.")]
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern FileSafeHandle CreateNamedPipe(String lpName, UInt32 dwOpenMode, UInt32 dwPipeMode, UInt32 nMaxInstances, UInt32 nOutBufferSize, UInt32 nInBufferSize, UInt32 nDefaultTimeOut, IntPtr lpSecurityAttributes);

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA5122:PInvokesShouldNotBeSafeCriticalFxCopRule", Justification = "Warning is bogus.")]
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern FileSafeHandle CreateNamedPipe(String lpName, UInt32 dwOpenMode, UInt32 dwPipeMode, UInt32 nMaxInstances, UInt32 nOutBufferSize, UInt32 nInBufferSize, UInt32 nDefaultTimeOut, ref SECURITY_ATTRIBUTES lpSecurityAttributes);

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA5122:PInvokesShouldNotBeSafeCriticalFxCopRule", Justification = "Warning is bogus.")]
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern Boolean DisconnectNamedPipe(FileSafeHandle hNamedPipe);

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA5122:PInvokesShouldNotBeSafeCriticalFxCopRule", Justification = "Warning is bogus.")]
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern Boolean FlushFileBuffers(FileSafeHandle hNamedPipe);

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA5122:PInvokesShouldNotBeSafeCriticalFxCopRule", Justification = "Warning is bogus.")]
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern Boolean PeekNamedPipe(FileSafeHandle hNamedPipe, Byte[] lpBuffer, UInt32 nBufferSize, ref UInt32 lpBytesRead, ref UInt32 lpTotalBytesAvail, ref UInt32 lpBytesLeftThisMessage);

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA5122:PInvokesShouldNotBeSafeCriticalFxCopRule", Justification = "Warning is bogus.")]
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern Boolean ReadFile(FileSafeHandle hFile, Byte[] lpBuffer, UInt32 nNumberOfBytesToRead, ref UInt32 lpNumberOfBytesRead, ref NativeOverlapped lpOverlapped);

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA5122:PInvokesShouldNotBeSafeCriticalFxCopRule", Justification = "Warning is bogus.")]
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern Boolean WriteFile(FileSafeHandle hFile, Byte[] lpBuffer, UInt32 nNumberOfBytesToWrite, ref UInt32 lpNumberOfBytesWritten, ref NativeOverlapped lpOverlapped);

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA5122:PInvokesShouldNotBeSafeCriticalFxCopRule", Justification = "Warning is bogus.")]
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern Boolean WaitNamedPipe(String lpNamedPipeName, UInt32 nTimeOut);

#pragma warning restore IDE0049 // Simplify Names
        }

    }
}


/* Server example
    var pipe = new Medo.IO.NamedPipe("TestPipe");
    pipe.Create();
    while (true) {
        pipe.Connect();
        while (pipe.IsAvailable == false) { Thread.Sleep(100); }
        var bytes = pipe.ReadAvailable();
        pipe.Write(bytes);
        pipe.Flush();
        pipe.Disconnect();
    }
 */

/* Client example
    var pipe = new Medo.IO.NamedPipe("TestPipe");
    pipe.Open();
    pipe.Write(System.Text.UTF8Encoding.UTF8.GetBytes("Always do what you have to do."));
    while (pipe.IsAvailable == false) { Thread.Sleep(100); }
    var bytes = pipe.ReadAvailable();
    Console.WriteLine(System.Text.UTF8Encoding.UTF8.GetString(bytes));
 */
