﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using Emgu.Models;
using System.Threading.Tasks;

namespace Emgu.TF.Models
{
    /// <summary>
    /// Resnet image recognition model
    /// </summary>
    public class Resnet : Emgu.TF.Util.UnmanagedObject
    {
        private FileDownloadManager _downloadManager;
        private Status _status = null;
        private SessionOptions _sessionOptions = null;
        private Session _session = null;
        private String[] _labels = null;
        private String _inputName = null;
        private String _outputName = null;
        private String _savedModelDir = null;

        /// <summary>
        /// Get the TF graph from the resnet model
        /// </summary>
        public Graph Graph
        {
            get
            {
                if (_session == null)
                    return null;
                return _session.Graph;
            }
        }

        /// <summary>
        /// Return true if the graph has been imported
        /// </summary>
        public bool Imported
        {
            get
            {
                return Graph != null;
            }
        }

        /// <summary>
        /// Get the MetaGraphDefBuffer
        /// </summary>
        public Buffer MetaGraphDefBuffer
        {
            get
            {
                if (_session == null)
                    return null;
                return _session.MetaGraphDefBuffer;
            }
        }

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID || UNITY_STANDALONE
        public double DownloadProgress
        {
            get
            {
                if (_downloadManager == null)
                    return 0;
                if (_downloadManager.CurrentWebClient == null)
                    return 1;
                return _downloadManager.CurrentWebClient.downloadProgress;
            }
        }

        public String DownloadFileName
        {
            get
            {
                if (_downloadManager == null)
                    return null;
                if (_downloadManager.CurrentWebClient == null)
                    return null;
                return _downloadManager.CurrentWebClient.url;
            }
        }
#endif

        /// <summary>
        /// Create a new inception object 
        /// </summary>
        /// <param name="status">The status object that can be used to keep track of error or exceptions</param>
        /// <param name="sessionOptions">The options for running the tensorflow session.</param>
        public Resnet(Status status = null, SessionOptions sessionOptions = null)
        {
            _status = status;
            _sessionOptions = sessionOptions;
            _downloadManager = new FileDownloadManager();

            _downloadManager.OnDownloadProgressChanged += onDownloadProgressChanged;
        }

        private void onDownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            if (OnDownloadProgressChanged != null)
                OnDownloadProgressChanged(sender, e);
        }

        /// <summary>
        /// Callback when the model download progress is changed.
        /// </summary>
        public event System.Net.DownloadProgressChangedEventHandler OnDownloadProgressChanged;

        /// <summary>
        /// Initiate the graph by checking if the model file exist locally, if not download the graph from internet.
        /// </summary>
        /// <param name="modelFile">The tensorflow graph.</param>
        /// <param name="labelFile">the object class labels.</param>
        /// <param name="inputName">The name of the input tensor</param>
        /// <param name="outputName">The name of the output tensor</param>
        public
#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID || UNITY_STANDALONE
            IEnumerator
#else
            async Task
#endif
            Init(
                DownloadableFile modelFile = null,
                DownloadableFile labelFile = null,
                String inputName = null,
                String outputName = null
            )
        {
            if (_session == null)
            {
                _inputName = inputName == null ? "serving_default_input_1" : inputName;
                _outputName = outputName == null ? "StatefulPartitionedCall" : outputName;

                _downloadManager.Clear();

                String defaultLocalSubfolder = "Resnet";
                if (modelFile == null)
                {
                    modelFile = new DownloadableFile(
                        "https://github.com/emgucv/models/raw/master/resnet/resnet_50_classification_1.zip",
                        defaultLocalSubfolder,
                        "861BA3BA5F18D8985A5611E5B668A0A020998762DD5A932BD4D0BCBBC1823A83"
                    );
                }

                if (labelFile == null)
                {
                    labelFile = new DownloadableFile(
                        "https://github.com/emgucv/models/raw/master/resnet/ImageNetLabels.txt",
                        defaultLocalSubfolder,
                        "536FEACC519DE3D418DE26B2EFFB4D75694A8C4C0063E36499A46FA8061E2DA9"
                    );
                }

                _downloadManager.AddFile(modelFile);
                _downloadManager.AddFile(labelFile);

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID || UNITY_STANDALONE
                yield return _downloadManager.Download();
#else
                await _downloadManager.Download();
#endif
                if (_downloadManager.AllFilesDownloaded)
                {
                    System.IO.FileInfo localZipFile = new System.IO.FileInfo(_downloadManager.Files[0].LocalFile);

                    _savedModelDir = System.IO.Path.Combine(localZipFile.DirectoryName, "SavedModel");
                    if (!System.IO.Directory.Exists(_savedModelDir))
                    {
                        System.IO.Directory.CreateDirectory(_savedModelDir);

                        System.IO.Compression.ZipFile.ExtractToDirectory(
                            localZipFile.FullName,
                            _savedModelDir);
                    }

                    CreateSession();
                } else
                {
                    System.Diagnostics.Trace.WriteLine("Failed to download files");
                }    
            }
        }

        /// <summary>
        /// Initiate the graph by checking if the model file exist locally, if not download the graph from internet.
        /// </summary>
        /// <param name="modelFiles">An array where the first file is the tensorflow graph and the second file are the object class labels. </param>
        /// <param name="downloadUrl">The url where the file can be downloaded</param>
        /// <param name="inputName">The input operation name. Default to "input" if not specified.</param>
        /// <param name="outputName">The output operation name. Default to "output" if not specified.</param>
        /// <param name="localModelFolder">The local folder to store the model</param>
        public
#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID || UNITY_STANDALONE
            System.Collections.IEnumerator
#else
            async Task
#endif
            Init(
                String[] modelFiles,
                String downloadUrl,
                String inputName = null,
                String outputName = null,
                String localModelFolder = "Resnet")
        {

            DownloadableFile[] downloadableFiles;
            if (modelFiles == null)
            {
                downloadableFiles = new DownloadableFile[2];
            }
            else
            {
                String url = downloadUrl == null
                    ? "https://github.com/emgucv/models/raw/master/resnet/"
                    : downloadUrl;
                String[] fileNames = modelFiles == null
                    ? new string[] { "resnet_50_classification_1.zip", "ImageNetLabels.txt" }
                    : modelFiles;
                downloadableFiles = new DownloadableFile[fileNames.Length];
                for (int i = 0; i < fileNames.Length; i++)
                    downloadableFiles[i] = new DownloadableFile(url + fileNames[i], localModelFolder);
            }

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID || UNITY_STANDALONE
                return Init(downloadableFiles[0], downloadableFiles[1], inputName, outputName);
#else
            await Init(downloadableFiles[0], downloadableFiles[1], inputName, outputName);
#endif

        }

        private void CreateSession()
        {
            if (_session != null)
                _session.Dispose();

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID || UNITY_STANDALONE
            UnityEngine.Debug.Log("Importing model");
#endif

            _session = new Session(
                _savedModelDir,
                new string[] { "serve" },
                _sessionOptions
            );

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID || UNITY_STANDALONE
            UnityEngine.Debug.Log("Model imported");
#endif

            _labels = File.ReadAllLines(_downloadManager.Files[1].LocalFile);
        }

        /// <summary>
        /// Pass the image tensor to the graph and return the probability that the object in image belongs to each of the object class.
        /// </summary>
        /// <param name="imageTensor">The tensor that contains the images to be classified</param>
        /// <returns>The object classes, sorted by probability from high to low</returns>
        public RecognitionResult[][] Recognize(Tensor imageTensor)
        {
            Operation input = _session.Graph[_inputName];
            if (input == null)
                throw new Exception(String.Format("Could not find input operation '{0}' in the graph", _inputName));

            Operation output = _session.Graph[_outputName];
            if (output == null)
                throw new Exception(String.Format("Could not find output operation '{0}' in the graph", _outputName));

            Tensor[] finalTensor = _session.Run(new Output[] { input }, new Tensor[] { imageTensor },
                new Output[] { output });
            float[,] probability = finalTensor[0].GetData(true) as float[,];

            int imageCount = probability.GetLength(0);
            int probLength = probability.GetLength(1);
            RecognitionResult[][] results = new RecognitionResult[imageCount][];
            for (int i = 0; i < imageCount; i++)
            {
                float[] p = new float[probLength];
                for (int j = 0; j < p.Length; j++)
                    p[j] = probability[i, j];
                results[i] = SortResults(p);
            }

            return results;
        }

        /// <summary>
        /// Sort the result from the most likely to the less likely
        /// </summary>
        /// <param name="probabilities">The probability for the classes, this should be the values of the output tensor</param>
        /// <returns>The recognition result, sorted by likelihood.</returns>
        public RecognitionResult[] SortResults(float[] probabilities)
        {
            if (probabilities == null)
                return null;

            if (_labels.Length != probabilities.Length)
                Trace.TraceWarning("Length of labels does not equals to the length of probabilities");

            RecognitionResult[] results = new RecognitionResult[Math.Min(_labels.Length, probabilities.Length)];
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = new RecognitionResult(_labels[(i + 1) % _labels.Length], probabilities[i]);
            }
            Array.Sort<RecognitionResult>(results, new Comparison<RecognitionResult>((a, b) => -a.Probability.CompareTo(b.Probability)));
            return results;
        }

        /// <summary>
        /// The result of the class labeling
        /// </summary>
        public class RecognitionResult
        {
            /// <summary>
            /// Create a recognition result by providing the label and the probability
            /// </summary>
            /// <param name="label">The label</param>
            /// <param name="probability">The probability</param>
            public RecognitionResult(String label, double probability)
            {
                Label = label;
                Probability = probability;
            }

            /// <summary>
            /// The label
            /// </summary>
            public String Label;
            /// <summary>
            /// The probability
            /// </summary>
            public double Probability;
        }

        /// <summary>
        /// Release the memory associated with the Resnet
        /// </summary>
        protected override void DisposeObject()
        {

            if (_session != null)
            {
                _session.Dispose();
                _session = null;
            }
        }
    }
}
