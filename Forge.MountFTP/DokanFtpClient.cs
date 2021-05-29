using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
//using AlexPilotti.FTPS.Common;
using Dokan;
using FluentFTP;

namespace Forge.MountFTP
{
    /// <summary>
    /// An FTP client implementing <see cref="Dokan.DokanOperations"/>,
    /// thus enabling it to be mounted using Dokan.
    /// </summary>
    class DokanFtpClient : DokanOperations
    {
        //string bufferedFileName;
        //byte[] bufferedFile;
        //int bufferedBytes;
        readonly FtpClient FtpClient;

        //readonly IFtpOptions ftpOptions;
        readonly Dictionary<string, DirectoryFileInformation> cachedDirectoryFileInformation = new Dictionary<string, DirectoryFileInformation>();
        readonly BlockingCollection<Task> FtpClientTaskQueue = new BlockingCollection<Task>();

        internal event LogEventHandler MethodCall, Debug;

        internal DokanFtpClient(FtpClient FtpClient, IFtpOptions ftpOptions)
        {
            this.FtpClient = FtpClient;
            this.FtpClient.Connect(new FtpProfile() {
                Host= ftpOptions.HostName,
                Credentials= new NetworkCredential(ftpOptions.UserName, ftpOptions.Password),
                Encoding= Encoding.UTF8,
                Timeout=5000,
            });
            //this.FtpClient = FtpClient;
            //this.ftpOptions = ftpOptions;

            const string ROOT = "\\";
            cachedDirectoryFileInformation.Add(
                ROOT,
                new DirectoryFileInformation(true)
                {
                    FileName = ROOT
                });

            //this.FtpClient.Connect(
            //    ftpOptions.HostName,
            //    new NetworkCredential(ftpOptions.UserName, ftpOptions.Password),
            //    ESSLSupportMode.ClearText);

            Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    FtpClientTaskQueue.Take().RunSynchronously();
                }
            });
        }

        #region DokanOperations

        public int Cleanup(string filename, DokanFileInfo info)
        {
            RaiseMethodCall("Cleanup " + filename);
            return 0;
        }

        public int CloseFile(string filename, DokanFileInfo info)
        {
            RaiseMethodCall("CloseFile " + filename);
            return 0;
        }

        public int CreateDirectory(string filename, DokanFileInfo info)
        {
            RaiseMethodCall("CreateDirectory " + filename);

            EnqueueTask(() =>
            {
                if (!cachedDirectoryFileInformation.ContainsKey(filename))
                {
                    FtpClient.CreateDirectory(filename);
                    //FtpClient.MakeDir(filename);
                    cachedDirectoryFileInformation[filename] = new DirectoryFileInformation(true) { FileName = filename };
                }
            }).Wait();

            return 0;
        }

        public int CreateFile(string filename, FileAccess access, FileShare share, FileMode mode, FileOptions options, DokanFileInfo info)
        {
            RaiseMethodCall(string.Format("CreateFile {0} FileMode: {1}", filename, mode));

            switch (mode)
            {
                case FileMode.Append:
                    break;
                case FileMode.Create:
                    break;
                case FileMode.CreateNew:
                    //EnqueueTask(() => FtpClient.PutFile(filename).Dispose()).Wait();
                    //EnqueueTask(() => FtpClient.UploadFile(filename).Dispose()).Wait();
                    return 0;
                case FileMode.Open:
                    if (cachedDirectoryFileInformation.ContainsKey(filename))
                    {
                        info.IsDirectory = cachedDirectoryFileInformation[filename].IsDirectory;
                        return 0;
                    }
                    else
                    {
                        RaiseDebug("CreateFile not cached: " + filename);
                        return -DokanNet.ERROR_FILE_NOT_FOUND;
                    }
                case FileMode.OpenOrCreate:
                    break;
                case FileMode.Truncate:
                    break;
                default:
                    break;
            }

            return -1;
        }

        public int DeleteDirectory(string filename, DokanFileInfo info)
        {
            RaiseMethodCall("DeleteDirectory " + filename);

            var filesToDelete = cachedDirectoryFileInformation
                .Where(kvp => kvp.Key.StartsWith(filename))
                .Where(kvp => kvp.Key != filename)
                .OrderByDescending(kvp => kvp.Key.Length)
                .ToArray();

            filesToDelete
                .Where(kvp => !kvp.Value.IsDirectory)
                .ForEach(kvp => DeleteFile(kvp.Key, info));

            filesToDelete
                .Where(kvp => kvp.Value.IsDirectory)
                .ForEach(kvp => DeleteDirectory(kvp.Key, info));

            EnqueueTask(() => FtpClient.DeleteDirectory(filename)).Wait();
            cachedDirectoryFileInformation.Remove(filename);

            return 0;
        }

        public int DeleteFile(string filename, DokanFileInfo info)
        {
            RaiseMethodCall("DeleteFile " + filename);

            EnqueueTask(() => FtpClient.DeleteFile(filename)).Wait();
            cachedDirectoryFileInformation.Remove(filename);

            return 0;
        }

        public int FindFiles(string filename, ArrayList files, DokanFileInfo info)
        {
            RaiseMethodCall("FindFiles " + filename);

            var parentDirectory = filename;
            const string BACKSLASH = "\\";
            if (!filename.EndsWith(BACKSLASH))
            {
                parentDirectory += BACKSLASH;
            }

            IList<FtpListItem> directoryList = null;
            EnqueueTask(() => directoryList = FtpClient.GetListing(GetUrl(filename))).Wait();
            RaiseMethodCall("FindFileResult " + directoryList.Count);

            directoryList
                .Select(dli => GetDirectoryFileInformation(parentDirectory, dli))
                .ForEach(dfi =>
                {
                    cachedDirectoryFileInformation[parentDirectory + dfi.FileName] = dfi;
                    files.Add(dfi);
                });

            return 0;
        }

        public int FlushFileBuffers(string filename, DokanFileInfo info)
        {
            RaiseMethodCall("FlushFileBuffers " + filename);
            return -1;
        }

        public int GetDiskFreeSpace(ref ulong freeBytesAvailable, ref ulong totalBytes, ref ulong totalFreeBytes, DokanFileInfo info)
        {
            RaiseMethodCall("GetDiskFreeSpace");

            totalFreeBytes =
            freeBytesAvailable = 1073741824; // == 1GB
            totalBytes = (ulong)cachedDirectoryFileInformation
                .Select(dfi => dfi.Value.Length)
                .Aggregate((sum, length) => sum += length)
                + totalFreeBytes;

            return 0;
        }

        public int GetFileInformation(string filename, FileInformation fileinfo, DokanFileInfo info)
        {
            RaiseMethodCall("GetFileInformation " + filename);

            if (cachedDirectoryFileInformation.ContainsKey(filename))
            {
                var cachedFileInfo = cachedDirectoryFileInformation[filename];

                fileinfo.FileName = cachedFileInfo.FileName;
                fileinfo.CreationTime = cachedFileInfo.CreationTime;
                fileinfo.LastAccessTime = cachedFileInfo.LastAccessTime;
                fileinfo.LastWriteTime = cachedFileInfo.LastWriteTime;
                fileinfo.Length = cachedFileInfo.Length;
                fileinfo.Attributes = cachedFileInfo.Attributes;

                return 0;
            }
            else
            {
                RaiseDebug("GetFileInformation not cached: " + filename);
                return -DokanNet.ERROR_FILE_NOT_FOUND;
            }
        }

        public int LockFile(string filename, long offset, long length, DokanFileInfo info)
        {
            RaiseMethodCall("LockFile " + filename);
            return -1;
        }

        public int MoveFile(string filename, string newname, bool replace, DokanFileInfo info)
        {
            RaiseMethodCall("MoveFile " + filename + " to " + newname);

            EnqueueTask(() => FtpClient.MoveFile(filename, newname)).Wait();

            return 0;
        }

        public int OpenDirectory(string filename, DokanFileInfo info)
        {
            RaiseMethodCall("OpenDirectory " + filename);
            return 0;
        }

        public int ReadFile(string filename, byte[] buffer, ref uint readBytes, long offset, DokanFileInfo info)
        {
            RaiseMethodCall("ReadFile " + filename);

            EnqueueTask(() =>
            {
                FtpClient.Download(out buffer, filename);
            }).Wait();
            readBytes = (uint)buffer.Length;
            return 0;
        }

        public int SetAllocationSize(string filename, long length, DokanFileInfo info)
        {
            RaiseMethodCall("SetAllocationSize " + filename);
            return -1;
        }

        public int SetEndOfFile(string filename, long length, DokanFileInfo info)
        {
            RaiseMethodCall("SetEndOfFile " + filename);

            cachedDirectoryFileInformation[filename] = new DirectoryFileInformation(false)
            {
                FileName = filename,
                Length = length
            };

            return 0;
        }

        public int SetFileAttributes(string filename, FileAttributes attr, DokanFileInfo info)
        {
            RaiseMethodCall("SetFileAttributes " + filename);
            return -1;
        }

        public int SetFileTime(string filename, DateTime ctime, DateTime atime, DateTime mtime, DokanFileInfo info)
        {
            RaiseMethodCall("SetFileTime " + filename);
            return -1;
        }

        public int UnlockFile(string filename, long offset, long length, DokanFileInfo info)
        {
            RaiseMethodCall("UnlockFile " + filename);
            return -1;
        }

        public int Unmount(DokanFileInfo info)
        {
            RaiseMethodCall("Unmount");
            return -1;
        }

        public int WriteFile(string filename, byte[] buffer, ref uint writtenBytes, long offset, DokanFileInfo info)
        {
            RaiseMethodCall("WriteFile " + filename);

            //long cachedLength = GetCachedLength(filename);

            EnqueueUploadTask(filename, buffer);
            writtenBytes = (uint)buffer.Length;

            return 0;
        }

        #endregion

        Task EnqueueTask(Action action)
        {
            var task = new Task(action);
            FtpClientTaskQueue.Add(task);
            return task;
        }

        void EnqueueUploadTask(string filename, byte[] buffer)
        {
            EnqueueTask(() =>
            {
                FtpClient.Upload(buffer, filename);
                //using (var ftpStream = FtpClient.PutFile(filename))
                //{
                //    ftpStream.Write(buffer, 0, buffer.Length);
                //}
            }).Wait();
        }

        void RaiseMethodCall(string message)
        {
            if (MethodCall != null)
            {
                MethodCall(this, new LogEventArgs(message));
            }
        }

        void RaiseDebug(string message)
        {
            if (Debug != null)
            {
                Debug(this, new LogEventArgs(message));
            }
        }

        static string GetUrl(string filename)
        {
            return filename.Replace('\\', '/');
        }

        DirectoryFileInformation GetDirectoryFileInformation(string parentDirectory, FtpListItem directoryListItem)
        {
            var path = parentDirectory + directoryListItem.Name;
            //try
            //{
                var lastWriteTime = directoryListItem.Type == FtpFileSystemObjectType.Directory ?
                FixDateTime(directoryListItem.RawCreated) :
                GetCachedLastWriteTime(path) ?? FixDateTime(directoryListItem.RawCreated);
                return new DirectoryFileInformation(directoryListItem)
                {
                    LastAccessTime = lastWriteTime,
                    LastWriteTime = lastWriteTime,
                    Length = directoryListItem.Type == FtpFileSystemObjectType.Directory ? default(long) : GetCachedLength(path),
                };
            //}
            //catch (Exception)
            //{ 

            //}
            return new DirectoryFileInformation(directoryListItem)
            {
                LastAccessTime = DateTime.Now,
                LastWriteTime = DateTime.Now,
                Length = default(long),
            };
        }

        DateTime? GetLastWriteTime(string fileName)
        {
            DateTime? fileModificationTime = null;
            EnqueueTask(() => fileModificationTime = FtpClient.GetModifiedTime(fileName)).Wait();
            return fileModificationTime;
        }

        DateTime? GetCachedLastWriteTime(string fileName)
        {
            return cachedDirectoryFileInformation.ContainsKey(fileName) ?
                cachedDirectoryFileInformation[fileName].LastWriteTime :
                GetLastWriteTime(fileName);
        }

        long GetLength(string fileName)
        {
            long fileTransferSize = default(long);
            EnqueueTask(new Action(() =>
            {
                try
                {
                    fileTransferSize = FtpClient.GetFileSize(fileName);
                }
                catch (Exception)
                {
                }
            })).Wait();
            return fileTransferSize;
        }

        long GetCachedLength(string fileName)
        {
            return cachedDirectoryFileInformation.ContainsKey(fileName) ?
                cachedDirectoryFileInformation[fileName].Length :
                GetLength(fileName);
        }

        class DirectoryFileInformation : FileInformation
        {
            bool isDirectory;
            internal bool IsDirectory
            {
                get
                {
                    return isDirectory;
                }
                private set
                {
                    isDirectory = value;
                    Attributes = value ?
                        FileAttributes.Directory :
                        FileAttributes.Normal;
                }
            }

            internal DirectoryFileInformation(bool isDirectory)
            {
                CreationTime = DateTime.Now;
                LastAccessTime = DateTime.Now;
                LastWriteTime = DateTime.Now;
                IsDirectory = isDirectory;
            }
            
            internal DirectoryFileInformation(FtpListItem directoryListItem)
            {
                FileName = directoryListItem.Name;
                var Created = FixDateTime(directoryListItem.RawCreated);
                var Modified = FixDateTime(directoryListItem.RawModified);
                CreationTime = Created;
                LastAccessTime = Modified;
                LastWriteTime = Modified;
                IsDirectory = directoryListItem.Type==FtpFileSystemObjectType.Directory;
            }
        }
        internal static DateTime FixDateTime(DateTime oldDateTime)
        {
            if (oldDateTime.Year < 1900)
            {
                return oldDateTime.AddYears(1900 - oldDateTime.Year);
            }
            return oldDateTime;
        }
    }
}