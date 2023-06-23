﻿//----------------------------------------------------------------------------
//  Copyright (C) 2004-2023 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Drawing;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net;
using Emgu.TF;
using Emgu.TF.Models;
using Emgu.Models;
using Tensorflow;

namespace Emgu.TF.XamarinForms
{
    public class MultiboxDetectionPage
#if __ANDROID__
        : AndroidCameraPage
#else
        : ButtonTextImagePage
#endif
    {
        private MultiboxGraph _multiboxGraph;

        private async Task InitMultibox(FileDownloadManager.DownloadProgressChangedEventHandler onProgressChanged)
        {
            if (_multiboxGraph == null)
            {
                SessionOptions so = new SessionOptions();
                Tensorflow.ConfigProto config = new Tensorflow.ConfigProto();
                if (TfInvoke.IsGoogleCudaEnabled)
                {
                    config.GpuOptions = new Tensorflow.GPUOptions();
                    config.GpuOptions.AllowGrowth = true;
                }
#if DEBUG
                config.LogDevicePlacement = true;
#endif
                so.SetConfig(config.ToProtobuf());
                _multiboxGraph = new MultiboxGraph(null, so);
                _multiboxGraph.OnDownloadProgressChanged += onProgressChanged;
                await _multiboxGraph.Init();
            }
        }

        public MultiboxDetectionPage()
           : base()
        {
            Title = "Multibox People Detection";
            this.TopButton.Text = "Detect People";

            this.TopButton.Clicked += async (sender, e) =>
            {
                try
                {
                    this.TopButton.IsEnabled = false;
                    SetMessage("Please wait...");
                    SetImage();

                    SetMessage("Please wait while we download and initialize the model.");
                    await InitMultibox(this.onDownloadProgressChanged);

                    if (_multiboxGraph == null || (!_multiboxGraph.Imported))
                    {
                        _multiboxGraph = null;
                        SetMessage("Failed to import multibox graph.");
                        return;
                    } else
                    {
                        SetMessage("Multibox graph imported.");
                    }

                    String[] images = await LoadImages(new string[] { "surfers.jpg" });
                    if (images == null)
                        return;

                    SetMessage("Image selected.");

                    Stopwatch watch = Stopwatch.StartNew();

                    Tensor imageTensor =
                        Emgu.TF.Models.ImageIO.ReadTensorFromImageFile<float>(images[0], 224, 224, 128.0f,
                            1.0f / 128.0f);
                    MultiboxGraph.Result[] detectResult = _multiboxGraph.Detect(imageTensor);
                    watch.Stop();
                    Emgu.Models.Annotation[] annotations = MultiboxGraph.FilterResults(detectResult, 0.1f);

                    SetMessage(String.Format("Detected in {0} milliseconds.", watch.ElapsedMilliseconds));
#if __ANDROID__
                    var bmp = Emgu.Models.NativeImageIO.ImageFileToBitmap(images[0], annotations);
                    SetImage(bmp);
#else
                    var jpeg = Emgu.Models.NativeImageIO.ImageFileToJpeg(images[0], annotations);
                    SetImage(jpeg.Raw, jpeg.Width, jpeg.Height);
//#if __MACOS__
//                    var displayImage = this.DisplayImage;
//                    displayImage.WidthRequest = jpeg.Width;
//                    displayImage.HeightRequest = jpeg.Height;
//#endif
#endif

                    
                }
                catch (Exception excpt)
                {
                    String msg = excpt.Message.Replace(System.Environment.NewLine, " ");
                    SetMessage(msg);
                }
                finally
                {
                    this.TopButton.IsEnabled = true;
                }
            };
        }
    }
}
