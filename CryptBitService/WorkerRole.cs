using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.Azure.KeyVault;
using Microsoft.WindowsAzure.Storage.Queue;
using CryptBitLibrary.Storage;
using CryptBitLibrary.DataEntities;
using CryptBitLibrary;
using Microsoft.WindowsAzure.Storage.Blob;
using System.IO;
using System.IO.Compression;

namespace CryptBitService
{
    public class WorkerRole : RoleEntryPoint
    {
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent runCompleteEvent = new ManualResetEvent(false);

        public override void Run()
        {
            Trace.TraceInformation("CryptBitService is running");

            try
            {
                this.RunAsync(this.cancellationTokenSource.Token).Wait();
            }
            finally
            {
                this.runCompleteEvent.Set();
            }
        }

        public override bool OnStart()
        {
            // Set the maximum number of concurrent connections
            ServicePointManager.DefaultConnectionLimit = 12;

            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            bool result = base.OnStart();

            Trace.TraceInformation("CryptBitService has been started");

            return result;
        }

        public override void OnStop()
        {
            Trace.TraceInformation("CryptBitService is stopping");

            this.cancellationTokenSource.Cancel();
            this.runCompleteEvent.WaitOne();

            base.OnStop();

            Trace.TraceInformation("CryptBitService has stopped");
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            
            while (!cancellationToken.IsCancellationRequested)
            {
                QueueStorage<string> processQueue = new QueueStorage<string>("processarchive");
                TableStorage<Archive> archiveClient = new TableStorage<Archive>("Archives");
                while (true)
                {

                    try
                    {


                        CloudQueueMessage message = processQueue.DequeueMessage(TimeSpan.FromMinutes(10));

                        if (message != null)
                        {
                            string archiveId = message.AsString.Trim('"');

                            Archive a = archiveClient.GetSingle(archiveId.Substring(0, 2), archiveId);

                            a.status = 2;
                            a.statusText = "Processing started.";
                            archiveClient.InsertOrMerge(a);

                            KeyVaultKeyResolver cloudResolver = new KeyVaultKeyResolver(CommonHelper.GetToken);
                            BlobEncryptionPolicy policy = new BlobEncryptionPolicy(null, cloudResolver);
                            BlobRequestOptions options = new BlobRequestOptions() { EncryptionPolicy = policy };


                            CloudBlobClient client = StorageHelper.storageAccount.CreateCloudBlobClient();
                            CloudBlobContainer container = client.GetContainerReference(a.RowKey);


                            using (var archiveStream = new MemoryStream())
                            {
                                using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true))
                                {
                                    foreach (IListBlobItem item in container.ListBlobs(null, false))
                                    {
                                        CloudBlockBlob blob = (CloudBlockBlob)item;

                                        var archiveFile = archive.CreateEntry(blob.Name, CompressionLevel.Optimal);

                                        using (var entryStream = archiveFile.Open())
                                        {
                                            blob.DownloadToStream(entryStream, null, options, null);
                                            a.statusText = string.Format("Processing {0}", blob.Name);
                                            archiveClient.InsertOrMerge(a);
                                            Console.WriteLine(blob.Name);
                                        }
                                    }
                                }


                                var key = CommonHelper.ResolveKey(a.archiveKey).Result;
                                policy = new BlobEncryptionPolicy(key, null);
                                options = new BlobRequestOptions() { EncryptionPolicy = policy };


                                CloudBlobContainer zipContainer = client.GetContainerReference("archives");
                                CloudBlockBlob zipBlob = zipContainer.GetBlockBlobReference(string.Format("{0}.zip", a.RowKey));
                                zipBlob.Properties.ContentType = "application/zip";

                                archiveStream.Seek(0, SeekOrigin.Begin);

                                zipBlob.UploadFromStream(archiveStream, archiveStream.Length, null, options, null);

                                //Delete the original container with all of the uploaded files
                                container.Delete();

                                a.status = 3;
                                a.statusText = "Processing complete.";
                                archiveClient.InsertOrMerge(a);
                            }
                            processQueue.DeleteMessage(message);
                        }


                    }
                    catch (Exception ex)
                    {
                        Logger.TrackException(ex, 0, "Error in worker role");
                    }
                    Thread.Sleep(1000);
                }
            }
        }
    }
}
