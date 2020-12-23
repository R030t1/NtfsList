using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alphaleonis.Win32.Filesystem;
using Alphaleonis.Win32.Vss;
using DiscUtils.Ntfs;
using Microsoft.Win32.SafeHandles;
using static PInvoke.Kernel32;

namespace NtfsList
{
    class VssSnapshot : IDisposable
    {
        IVssBackupComponents _backup;
        VssSnapshotProperties _properties;
        public Guid _set_id;
        public Guid _snapshot_id;

        public string Root
        {
            get
            {
                if (_properties == null)
                    _properties = _backup.GetSnapshotProperties(_snapshot_id);
                return _properties.SnapshotDeviceObject;
            }
        }

        public VssSnapshot(IVssBackupComponents backup)
        {
            _backup = backup;
            _set_id = backup.StartSnapshotSet();
        }

        public void AddVolume(string name)
        {
            if (_backup.IsVolumeSupported(name))
                _snapshot_id = _backup.AddToSnapshotSet(name);
            else throw new VssVolumeNotSupportedException(name);
        }

        public void Copy()
        {
            _backup.DoSnapshotSet();
        }

        public void Dispose()
        {
            try { _backup.DeleteSnapshotSet(_set_id, false); } catch { }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            IVssFactory vssImpl = VssFactoryProvider.Default.GetVssFactory();
            using (var backup = vssImpl.CreateVssBackupComponents())
            {
                backup.InitializeForBackup(null);
                backup.GatherWriterMetadata();
                backup.SetBackupState(false, true, VssBackupType.Full, false);

                var snap = new VssSnapshot(backup);
                snap.AddVolume(@"C:\");

                backup.PrepareForBackup();
                snap.Copy();

                foreach (var prop in backup.QuerySnapshots())
                {
                    // Filter out already existing snapshots.
                    if (snap._snapshot_id != prop.SnapshotId)
                        continue;

                    var h = CreateFile(
                        prop.SnapshotDeviceObject,
                        ACCESS_MASK.GenericRight.GENERIC_READ, FileShare.FILE_SHARE_READ, IntPtr.Zero,
                        CreationDisposition.OPEN_EXISTING, CreateFileFlags.FILE_ATTRIBUTE_NORMAL, new SafeObjectHandle()
                    );

                    Console.WriteLine("Volume Shadow: {0}", prop.SnapshotDeviceObject);
                    var stream = new System.IO.FileStream(new SafeFileHandle(h.DangerousGetHandle(), true), System.IO.FileAccess.Read);

                    try
                    {
                        var fs = new NtfsFileSystem(stream);
                        Console.WriteLine("Opened: {0}", prop.SnapshotDeviceObject);
                    }
                    catch
                    {
                        Console.WriteLine("Failed to open: {0}", prop.SnapshotDeviceObject);
                    }
                    finally
                    {
                    }
                }

                snap.Dispose();
                backup.FreeWriterMetadata();
                backup.BackupComplete(); // May throw exception, ignorable.
            }
            Console.Read();
        }
    }
}
