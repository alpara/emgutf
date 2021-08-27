﻿//----------------------------------------------------------------------------
//  Copyright (C) 2004-2021 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using Emgu.Models;
using Emgu.TF;

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID || UNITY_STANDALONE
using UnityEngine;
#elif __ANDROID__
using Android.Graphics;
#elif __IOS__
using CoreGraphics;
using UIKit;
#elif __MACOS__
using AppKit;
using CoreGraphics;
#endif

namespace Emgu.TF.Models
{
    /// <summary>
    /// Provide image IO functions
    /// </summary>
    public class ImageIO
    {
        /// <summary>
        /// Encode the tensor to JPEG image
        /// </summary>
        /// <param name="image">The image tensor. Should be a single channel or 3 channel 4-D tensor</param>
        /// <param name="scale">The tensor value will be scaled with this values first</param>
        /// <param name="inputMean">The mean value will be added back to the image after scaling is done</param>
        /// <returns>The jpeg data</returns>
        private static byte[] EncodeJpeg(Tensor image, float scale = 1.0f, float inputMean = 0.0f)
        {
            var graph = new Graph();
            Operation input = graph.Placeholder(DataType.Float);

            Tensor scaleTensor = new Tensor(scale);
            Operation scaleOp = graph.Const(scaleTensor, scaleTensor.Type, opName: "scale");
            Operation scaled = graph.Mul(input, scaleOp);
            Tensor mean = new Tensor(inputMean);
            Operation meanOp = graph.Const(mean, mean.Type, opName: "mean");
            Operation added = graph.Add(scaled, meanOp);
            Operation uintCaster = graph.Cast(added, DstT: DataType.Uint8); //cast to uint
            Operation squeezed = graph.Squeeze(uintCaster, new long[] { 0 });
            Operation jpegRaw = graph.EncodeJpeg(squeezed);
            Session session = new Session(graph);

            Tensor[] raw = session.Run(new Output[] { input }, new Tensor[] { image },
                new Output[] { jpegRaw });

            return raw[0].DecodeString();
        }

        /// <summary>
        /// Convert the tensor that contains pixels values to an array of bytes
        /// </summary>
        /// <param name="imageTensor">Image tensor. Only [1, height, width, 3] tensor input type is supported.</param>
        /// <param name="scale">The scale that will be used to multiple the pixel values</param>
        /// <param name="mean">The mean value that will be added back to the pixels</param>
        /// <param name="dstChannels">Can be 3 or 4. If 4 channels, the last channel is filled with 255</param>
        /// <param name="status">The status</param>
        /// <returns>The array of bytes that contains the pixel value of the tensor</returns>
        public static byte[] TensorToPixel(Tensor imageTensor, float scale = 1.0f, float mean = 0.0f, int dstChannels = 3, Status status = null)
        {
            int[] dim = imageTensor.Dim;
            if (dim[0] != 1 || dim[3] != 3)
            {
                throw new NotImplementedException("Only [1, height, width, 3] tensor input type is supported.");
            }

            using (StatusChecker checker = new StatusChecker(status))
            {
                var graph = new Graph();
                Operation input = graph.Placeholder(imageTensor.Type);

                Operation floatCaster;
                if (imageTensor.Type != DataType.Float)
                {
                    floatCaster = graph.Cast(input, DstT: DataType.Float);
                } else
                {
                    floatCaster = input;
                }

                //multiply with scale
                Tensor scaleTensor = new Tensor(scale);
                Operation scaleOp = graph.Const(scaleTensor, scaleTensor.Type, opName: "scale");
                Operation scaled = graph.Mul(floatCaster, scaleOp);

                //Add mean value
                Tensor meanTensor = new Tensor(mean);
                Operation meanOp = graph.Const(meanTensor, meanTensor.Type, opName: "mean");
                Operation added = graph.Add(scaled, meanOp);

                //cast to byte
                Operation byteCaster = graph.Cast(added, DstT: DataType.Uint8);

                //run the graph
                using (Session session = new Session(graph))
                {
                    Tensor[] imageResults = session.Run(new Output[] {input}, new Tensor[] {imageTensor},
                        new Output[] {byteCaster});

                    //get the raw data
                    byte[] raw = imageResults[0].Flat<byte>();

                    if (dstChannels == 3)
                    {
                        return raw;
                    }
                    else if (dstChannels == 4)
                    {
                        int pixelCount = raw.Length / 3;
                        byte[] colors = new byte[pixelCount * 4];
                        int idxColor = 0;
                        int idxRaw = 0;
                        for (int i = 0; i < pixelCount; i++)
                        {
                            colors[idxColor++] = raw[idxRaw++];
                            colors[idxColor++] = raw[idxRaw++];
                            colors[idxColor++] = raw[idxRaw++];
                            colors[idxColor++] = (byte) 255;
                        }
                        return colors;
                    }
                    else
                    {
                        throw new NotImplementedException(String.Format("Output channel count of {0} is not supported",
                            dstChannels));
                    }
                }
            }
        }

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID || UNITY_STANDALONE

        public static Tensor ReadTensorFromImageFile(String fileName, int inputHeight = -1, int inputWidth = -1, float inputMean = 0.0f, float scale = 1.0f, bool flipUpsideDown = false)
        {
            Texture2D texture = NativeImageIO.ReadTexture2DFromFile(fileName);
            return ReadTensorFromTexture2D(texture, inputHeight, inputWidth, inputMean, scale, flipUpsideDown);
        }

        public static Tensor ReadTensorFromTexture2D(
            Texture2D texture, int inputHeight = -1, int inputWidth = -1,
            float inputMean = 0.0f, float scale = 1.0f, bool flipUpsideDown = false)
        {
#region Get the RGBA raw data as imgOrig
            Tensor imgOrig = new Tensor(DataType.Uint8, new int[] { 1, texture.height, texture.width, 4 });
            Color32[] colors = texture.GetPixels32(); //32bit RGBA
            GCHandle colorsHandle = GCHandle.Alloc(colors, GCHandleType.Pinned);
            Emgu.TF.TfInvoke.tfeMemcpy(imgOrig.DataPointer, colorsHandle.AddrOfPinnedObject(), colors.Length * Marshal.SizeOf(typeof(Color32)));
            colorsHandle.Free();
#endregion

            var graph = new Graph();
            Operation input = graph.Placeholder(DataType.Uint8);

            #region Cast to float
            Operation floatCaster = graph.Cast(input, DstT: DataType.Float); //cast to float
            #endregion

            #region slice out the alpha channel
            Tensor sliceBegin = new Tensor(new int[] { 0, 0, 0, 0 });
            Operation sliceBeginOp = graph.Const(sliceBegin, sliceBegin.Type, opName: "sliceBegin");
            Tensor sliceSize = new Tensor(new int[] { 1, -1, -1, 3 });
            Operation sliceSizeOp = graph.Const(sliceSize, sliceSize.Type, opName: "sliceSize");
            Operation sliced = graph.Slice(floatCaster, sliceBeginOp, sliceSizeOp, "slice");
            #endregion

            #region crop and resize image
            Tensor boxes = new Tensor(DataType.Float, new int[] {1, 4});
            float[] boxCorners;
            if (flipUpsideDown)
            {
                boxCorners = new float[] { 1f, 0f, 0f, 1f }; //y1, x1, y2, x2    
            }
            else
            {
                boxCorners = new float[] { 0f, 0f, 1f, 1f }; //y1, x1, y2, x2    
            }
            Marshal.Copy(boxCorners, 0, boxes.DataPointer, boxCorners.Length);
            Operation boxesOp = graph.Const(boxes, boxes.Type, "boxes");
            

            Tensor boxIdx = new Tensor(new int[] {0});
            Operation boxIdxOp = graph.Const(boxIdx, boxIdx.Type, "boxIdx");
            int width, height;
            if (inputHeight > 0 || inputWidth > 0)
            {
                width = inputWidth;
                height = inputHeight;
            }
            else
            {
                width = texture.width;
                height = texture.height;
            }
            Tensor cropSize = new Tensor(new int[] {height, width });
            Operation cropSizeOp = graph.Const(cropSize, cropSize.Type, "cropSize");
            Operation resized = graph.CropAndResize(sliced, boxesOp, boxIdxOp, cropSizeOp);
            #endregion

            Tensor mean = new Tensor(inputMean);
            Operation meanOp = graph.Const(mean, mean.Type, opName: "mean");
            Operation substracted = graph.Sub(resized, meanOp);

            Tensor scaleTensor = new Tensor(scale);
            Operation scaleOp = graph.Const(scaleTensor, scaleTensor.Type, opName: "scale");
            Operation scaled = graph.Mul(substracted, scaleOp);

            //Operation scaled = graph.
            Session session = new Session(graph);

            Tensor[] imageResults = session.Run(new Output[] { input }, new Tensor[] { imgOrig },
                new Output[] { scaled });
            return imageResults[0];
        }


        public static Texture2D Resize(Texture2D source, int newWidth, int newHeight)
        {
            source.filterMode = FilterMode.Bilinear;
            RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight);
            rt.filterMode = FilterMode.Bilinear;
            
            RenderTexture.active = rt;
            Graphics.Blit(source, rt);
            var nTex = new Texture2D(newWidth, newHeight);
            nTex.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
            nTex.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            return nTex;
        }

        /*
        public static Tensor ReadTensorFromTexture2D_V0(
            Texture2D texture, int inputHeight = -1, int inputWidth = -1,
            float inputMean = 0.0f, float scale = 1.0f, bool flipUpsideDown = false)
        {
            Color32[] colors;
            Tensor t;
            int width, height;
            if (inputHeight > 0 || inputWidth > 0)
            {
                Texture2D small = Resize(texture, inputWidth, inputHeight);
                colors = small.GetPixels32();
                width = inputWidth;
                height = inputHeight;
            }
            else
            {
                width = texture.width;
                height = texture.height;
                colors = texture.GetPixels32();
            }
            t = new Tensor(DataType.Float, new int[] { 1, height, width, 3 });

            float[] floatValues = new float[colors.Length*3];

            if (flipUpsideDown)
            {
                for (int i = 0; i < height; i++)
                for (int j = 0; j < width; j++)
                {
                    Color32 val = colors[(height - i - 1) * width + j];
                    int idx = i * width + j;
                    floatValues[idx * 3 + 0] = (val.r - inputMean) * scale;
                    floatValues[idx * 3 + 1] = (val.g - inputMean) * scale;
                    floatValues[idx * 3 + 2] = (val.b - inputMean) * scale;
                }
            }
            else
            {
                for (int i = 0; i < colors.Length; ++i)
                {
                    Color32 val = colors[i];
                    floatValues[i * 3 + 0] = (val.r - inputMean) * scale;
                    floatValues[i * 3 + 1] = (val.g - inputMean) * scale;
                    floatValues[i * 3 + 2] = (val.b - inputMean) * scale;
                }
            }
            

            System.Runtime.InteropServices.Marshal.Copy(floatValues, 0, t.DataPointer, floatValues.Length);

            return t;
        }*/
#else

        /// <summary>
        /// Encode the tensor to raw jpeg data
        /// </summary>
        /// <param name="image">The image tensor</param>
        /// <param name="scale">The scale used to multiply with the pixel values</param>
        /// <param name="mean">The mean value that will be added to the pixel values</param>
        /// <param name="status">Optional status</param>
        /// <returns>The raw jpeg data converted from the image tensor</returns>
        public static byte[] TensorToJpeg(Tensor image, float scale = 1.0f, float mean = 0.0f, Status status = null)
        {
#if __ANDROID__         
            byte[] rawPixel = TensorToPixel(image, scale, mean, 4);
            int[] dim = image.Dim;
            return NativeImageIO.PixelToJpeg(rawPixel, dim[2], dim[1], 4).Raw;
#elif __IOS__
            if (mean != 0.0)
                throw new NotImplemenetedException("Not able to accept mean values on this platform");
            byte[] rawPixel = TensorToPixel(image, scale, mean, 3);
            int[] dim = image.Dim;
            return NativeImageIO.PixelToJpeg(rawPixel, dim[2], dim[1], 3).Raw;
#elif __MACOS__
            byte[] rawPixel = TensorToPixel(image, scale, mean, 4);
            int[] dim = image.Dim;
            return NativeImageIO.PixelToJpeg(rawPixel, dim[2], dim[1], 4).Raw;
#else
            return EncodeJpeg(image, scale, mean);
#endif
        }

        /// <summary>
        /// Read the image file into a Tensorflow tensor
        /// </summary>
        /// <typeparam name="T">The tensor data type, e.g. float</typeparam>
        /// <param name="fileName">The name of the image file</param>
        /// <param name="inputHeight">The height of the input tensor. If zero or negative, will use the image height from the file</param>
        /// <param name="inputWidth">The width of the input tensor. If zero or negative, will use the image width from the file</param>
        /// <param name="inputMean">The input mean, will be subtracted from the image pixel value</param>
        /// <param name="scale">The optional scale, after input means is substracted, the pixel value will multiply with the scale to produce the tensor value</param>
        /// <param name="flipUpSideDown">If true, the image will be flipped upside down</param>
        /// <param name="swapBR">If true, the blue and red channels will be swapped</param>
        /// <returns>The tensorflow tensor.</returns>
        public static Tensor ReadTensorFromImageFile<T>(
            String fileName, 
            int inputHeight = -1, 
            int inputWidth = -1, 
            float inputMean = 0.0f, 
            float scale = 1.0f,
            bool flipUpSideDown = false,
            bool swapBR = false) where T: struct
        {
#if __ANDROID__
            return NativeReadTensorFromImageFile<T>(fileName, inputHeight, inputWidth, inputMean, scale,
                flipUpSideDown, swapBR);
#elif __IOS__
            UIImage image = new UIImage(fileName);
			if (inputHeight > 0 || inputWidth > 0)
			{
                UIImage resized = image.Scale(new CGSize(inputWidth, inputHeight));
                image.Dispose();
				image = resized;
			}
            int[] intValues = new int[(int) (image.Size.Width * image.Size.Height)];
            float[] floatValues = new float[(int) (image.Size.Width * image.Size.Height * 3)];
            System.Runtime.InteropServices.GCHandle handle = System.Runtime.InteropServices.GCHandle.Alloc(intValues, System.Runtime.InteropServices.GCHandleType.Pinned);
            using (CGImage cgimage = image.CGImage)
            using (CGColorSpace cspace = CGColorSpace.CreateDeviceRGB())
            using (CGBitmapContext context = new CGBitmapContext(
                handle.AddrOfPinnedObject(),
                (nint)image.Size.Width,
                (nint)image.Size.Height,
                8,
                (nint)image.Size.Width * 4,
                cspace,
                CGImageAlphaInfo.PremultipliedLast
                ))
            {
                context.DrawImage(new CGRect(new CGPoint(), image.Size), cgimage);

            }
            handle.Free();

			for (int i = 0; i < intValues.Length; ++i)
			{
				int val = intValues[i];
				floatValues[i * 3 + 0] = (((val >> 16) & 0xFF) - inputMean) * scale;
				floatValues[i * 3 + 1] = (((val >> 8) & 0xFF) - inputMean) * scale;
				floatValues[i * 3 + 2] = ((val & 0xFF) - inputMean) * scale;
			}

            Tensor t = new Tensor(DataType.Float, new int[] { 1, (int)image.Size.Height, (int) image.Size.Width, 3 });
			System.Runtime.InteropServices.Marshal.Copy(floatValues, 0, t.DataPointer, floatValues.Length);
			return t;
#else
            FileInfo fi = new FileInfo(fileName);
            String extension = fi.Extension.ToLower();

            //Use tensorflow to decode the following image formats
            if ((typeof(T) == typeof(float)) 
                &&
                (extension.Equals(".jpeg") 
                || extension.Equals(".jpg") 
                || extension.Equals(".png")
                || extension.Equals(".gif")))
            {
                return DecodeImageToTensor32F3C(fileName, inputHeight, inputWidth, inputMean, scale, flipUpSideDown, swapBR);
            }
            else
            {
                return NativeReadTensorFromImageFile<T>(fileName, inputHeight, inputWidth, inputMean, scale,
                    flipUpSideDown, swapBR);
            }
#endif
        }

        /// <summary>
        /// Read the image files into a Tensorflow tensor
        /// </summary>
        /// <typeparam name="T">The tensor data type, e.g. float</typeparam>
        /// <param name="fileNames">The name of the image files</param>
        /// <param name="inputHeight">The height of the input tensor. If zero or negative, will use the image height from the file</param>
        /// <param name="inputWidth">The width of the input tensor. If zero or negative, will use the image width from the file</param>
        /// <param name="inputMean">The input mean, will be subtracted from the image pixel value</param>
        /// <param name="scale">The optional scale, after input means is subtracted, the pixel value will multiply with the scale to produce the tensor value</param>
        /// <param name="flipUpSideDown">If true, the image will be flipped upside down</param>
        /// <param name="swapBR">If true, the blue and red channels will be swapped</param>
        /// <returns>The tensorflow tensor.</returns>
        public static Tensor ReadTensorFromImageFiles<T>(
            String[] fileNames,
            int inputHeight = -1,
            int inputWidth = -1,
            float inputMean = 0.0f,
            float scale = 1.0f,
            bool flipUpSideDown = false,
            bool swapBR = false) where T : struct
        {
#if __ANDROID__
            throw new NotImplementedException("Not implemented for this platform");
#elif __IOS__
            throw new NotImplementedException("Not implemented for this platform");
#else
            Tensor[] tensors = new Tensor[fileNames.Length];

            for (int i = 0; i < tensors.Length; i++)
            {
                String fileName = fileNames[i];
                FileInfo fi = new FileInfo(fileName);
                String extension = fi.Extension.ToLower();

                //Use tensorflow to decode the following image formats
                if ((typeof(T) == typeof(float))
                    &&
                    (extension.Equals(".jpeg")
                     || extension.Equals(".jpg")
                     || extension.Equals(".png")
                     || extension.Equals(".gif")))
                {
                    tensors[i] = DecodeImageToTensor32F3C(fileName, inputHeight, inputWidth, inputMean, scale, flipUpSideDown,
                        swapBR);
                }
                else
                {
                    tensors[i] = NativeReadTensorFromImageFile<T>(fileName, inputHeight, inputWidth, inputMean, scale,
                        flipUpSideDown, swapBR);
                }
            }

            if (tensors.Length == 1)
                return tensors[0];
            else
            {   //stack the tensors
                return StackTensors(tensors);
            }
#endif
        }

        private static int GetSize(int[] dimension)
        {
            if (dimension.Length == 0)
                return 0;
            int s = 1;
            for (int i = 0; i < dimension.Length; i++)
            {
                s = s * dimension[i];
            }
            return s;
        }

        private static Tensor StackTensors(Tensor[] tensors)
        {
            var dimension = tensors[0].Dim;
            var finalDimension = new int[dimension.Length];
            finalDimension[0] = tensors.Length;
            for (int i = 1; i < dimension.Length; i++)
            {
                finalDimension[i] = dimension[i];
            }

            Tensor result = new Tensor(tensors[0].Type, finalDimension);
            IntPtr dataPtr = result.DataPointer;
            for (int i = 0; i < tensors.Length; i++)
            {
                int s = tensors[i].ByteSize;
                Emgu.TF.TfInvoke.Memcpy(dataPtr, tensors[i].DataPointer, s);
                dataPtr = new IntPtr(dataPtr.ToInt64() + s);
            }
            return result;
        }

        private static Tensor DecodeImageToTensor32F3C(
            String fileName,
            int inputHeight = -1,
            int inputWidth = -1,
            float inputMean = 0.0f,
            float scale = 1.0f,
            bool flipUpSideDown = false,
            bool swapBR = false)
        {
            using (Graph graph = new Graph())
            {
                Operation input = graph.Placeholder(DataType.String);

                //output dimension [height, width, 3] where 3 is the number of channels
                //DecodeJpeg can decode JPEG, PNG and GIF
                Operation jpegDecoder = graph.DecodeJpeg(input, 3);

                Operation floatCaster = graph.Cast(jpegDecoder, DstT: DataType.Float); //cast to float

                Tensor zeroConst = new Tensor(0);
                Operation zeroConstOp = graph.Const(zeroConst, zeroConst.Type, opName: "zeroConstOp");
                Operation dimsExpander = graph.ExpandDims(floatCaster, zeroConstOp); //turn it to dimension [1, height, width, 3]

                Operation resized;
                bool resizeRequired = (inputHeight > 0) && (inputWidth > 0);
                if (resizeRequired)
                {
                    Tensor size = new Tensor(new int[] { inputHeight, inputWidth }); // new size;
                    Operation sizeOp = graph.Const(size, size.Type, opName: "size");
                    resized = graph.ResizeBilinear(dimsExpander, sizeOp); //resize image
                }
                else
                {
                    resized = dimsExpander;
                }

                Tensor mean = new Tensor(inputMean);
                Operation meanOp = graph.Const(mean, mean.Type, opName: "mean");
                Operation subtracted = graph.Sub(resized, meanOp);

                Tensor scaleTensor = new Tensor(scale);
                Operation scaleOp = graph.Const(scaleTensor, scaleTensor.Type, opName: "scale");
                Operation scaled = graph.Mul(subtracted, scaleOp);

                Operation swapedBR;
                if (swapBR)
                {
                    Tensor threeConst = new Tensor(new int[] { 3 });
                    Operation threeConstOp = graph.Const(threeConst, threeConst.Type, "threeConstOp");
                    swapedBR = graph.ReverseV2(scaled, threeConstOp, "swapBR");
                }
                else
                {
                    swapedBR = scaled;
                }

                Operation flipped;
                if (flipUpSideDown)
                {
                    Tensor oneConst = new Tensor(new int[] { 1 });
                    Operation oneConstOp = graph.Const(oneConst, oneConst.Type, "oneConstOp");
                    flipped = graph.ReverseV2(swapedBR, oneConstOp, "flipUpSideDownOp");
                }
                else
                {
                    flipped = swapedBR;
                }

                using (Session session = new Session(graph))
                {
                    Tensor imageTensor = Tensor.FromString(File.ReadAllBytes(fileName));

                    Tensor[] imageResults = session.Run(new Output[] { input }, new Tensor[] { imageTensor },
                        new Output[] { flipped });
                    return imageResults[0];
                }
            }
        }

        
        private static Tensor NativeReadTensorFromImageFile<T>(
            String fileName,
            int inputHeight = -1,
            int inputWidth = -1,
            float inputMean = 0.0f,
            float scale = 1.0f,
            bool flipUpSideDown = false,
            bool swapBR = false,
            Status status = null) where T : struct
        {
            return NativeReadTensorFromImageFiles<T>(
                new string[] {fileName},
                inputHeight,
                inputWidth,
                inputMean,
                scale,
                flipUpSideDown,
                swapBR,
                status);
        }

        /// <summary>
        /// Read image files, covert the data and save it to the native pointer
        /// </summary>
        /// <typeparam name="T">The type of the data to covert the image pixel values to. e.g. "float" or "byte"</typeparam>
        /// <param name="fileNames">The name of the image files</param>
        /// <param name="inputHeight">The height of the image, must match the height requirement for the tensor</param>
        /// <param name="inputWidth">The width of the image, must match the width requirement for the tensor</param>
        /// <param name="inputMean">The mean value, it will be subtracted from the input image pixel values</param>
        /// <param name="scale">The scale, after mean is subtracted, the scale will be used to multiply the pixel values</param>
        /// <param name="flipUpSideDown">If true, the image needs to be flipped up side down</param>
        /// <param name="swapBR">If true, will flip the Blue channel with the Red. e.g. If false, the tensor's color channel order will be RGB. If true, the tensor's color channle order will be BGR </param>
        /// <param name="status">Tensorflow status</param>
        /// <returns>The tensor that contains all the image files</returns>
        private static Tensor NativeReadTensorFromImageFiles<T>(
            String[] fileNames,
            int inputHeight = -1,
            int inputWidth = -1,
            float inputMean = 0.0f,
            float scale = 1.0f,
            bool flipUpSideDown = false,
            bool swapBR = false,
            Status status = null) where T : struct
        {
            if (fileNames.Length == 0)
                throw new ArgumentException("Intput file names do not contain any files");

            String fileName = fileNames[0];
            if (!File.Exists(fileName))
                throw new FileNotFoundException(String.Format("File {0} do not exist.", fileName));

            IntPtr dataPtr;
            int step;
            Tensor t;

#if __ANDROID__
            using (BitmapFactory.Options options = new BitmapFactory.Options())
            {
                options.InPreferredConfig = Android.Graphics.Bitmap.Config.Argb8888; //Prefer ARGB8888 format
                using (Android.Graphics.Bitmap bmp0 = Android.Graphics.BitmapFactory.DecodeFile(fileName, options))
                {
                    if (inputHeight <= 0)
                        inputHeight = bmp0.Height;
                    if (inputWidth <= 0)
                        inputWidth = bmp0.Width;
                    if (typeof(T) == typeof(float))
                        t = new Tensor(DataType.Float,
                            new int[] {fileNames.Length, (int) inputHeight, (int) inputWidth, 3});
                    else if (typeof(T) == typeof(byte))
                        t = new Tensor(DataType.Uint8,
                            new int[] {fileNames.Length, (int) inputHeight, (int) inputWidth, 3});
                    else
                    {
                        throw new Exception(String.Format("Conversion to tensor of type {0} is not implemented",
                            typeof(T)));
                    }

                    dataPtr = t.DataPointer;
                    step = NativeImageIO.ReadBitmapToTensor<T>(
                        bmp0,
                        dataPtr,
                        inputHeight,
                        inputWidth,
                        inputMean,
                        scale,
                        flipUpSideDown,
                        swapBR);
                    dataPtr = new IntPtr(dataPtr.ToInt64() + step);
                }

                for (int i = 1; i < fileNames.Length; i++)
                {
                    fileName = fileNames[i];
                    if (!File.Exists(fileName))
                        throw new FileNotFoundException(String.Format("File {0} do not exist.", fileName));

                    //Read the file using Bitmap class
                    using (Android.Graphics.Bitmap bmp = Android.Graphics.BitmapFactory.DecodeFile(fileName, options))
                    {
                        step = NativeImageIO.ReadBitmapToTensor<T>(
                            bmp, 
                            dataPtr, 
                            inputHeight, 
                            inputWidth, 
                            inputMean,
                            scale,
                            flipUpSideDown, 
                            swapBR);

                        dataPtr = new IntPtr(dataPtr.ToInt64() + step);
                    }

                }
            }
#elif __MACOS__
            using (NSImage image0 = new NSImage(fileName))
            {
                if (inputHeight <= 0)
                    inputHeight = (int)image0.Size.Height;
                if (inputWidth <= 0)
                    inputWidth = (int) image0.Size.Width;
                if (typeof(T) == typeof(float))
                    t = new Tensor(DataType.Float,
                        new int[] { fileNames.Length, (int)inputHeight, (int)inputWidth, 3 });
                else if (typeof(T) == typeof(byte))
                    t = new Tensor(DataType.Uint8,
                        new int[] { fileNames.Length, (int)inputHeight, (int)inputWidth, 3 });
                else
                {
                    throw new Exception(String.Format("Conversion to tensor of type {0} is not implemented",
                        typeof(T)));
                }

                dataPtr = t.DataPointer;
                step = NativeImageIO.ReadImageToTensor<T>(
                    image0, dataPtr, inputHeight, inputWidth, inputMean, scale,
                    flipUpSideDown, swapBR);
                dataPtr = new IntPtr(dataPtr.ToInt64() + step);
            }

            for (int i = 1; i < fileNames.Length; i++)
            {
                fileName = fileNames[i];
                if (!File.Exists(fileName))
                    throw new FileNotFoundException(String.Format("File {0} do not exist.", fileName));

                //Read the file using Bitmap class
                using (NSImage image = new NSImage(fileName))
                {
                    step = NativeImageIO.ReadImageToTensor<T>(image, dataPtr, inputHeight, inputWidth, inputMean, scale,
                        flipUpSideDown, swapBR);

                    dataPtr = new IntPtr(dataPtr.ToInt64() + step);
                }

            }
#else
            using (System.Drawing.Bitmap bmp0 = new System.Drawing.Bitmap(fileName))
            {
                if (inputHeight <= 0)
                    inputHeight = bmp0.Height;
                if (inputWidth <= 0)
                    inputWidth = bmp0.Width;
                if (typeof(T) == typeof(float))
                    t = new Tensor(DataType.Float,
                        new int[] { fileNames.Length, (int)inputHeight, (int)inputWidth, 3 });
                else if (typeof(T) == typeof(byte))
                    t = new Tensor(DataType.Uint8,
                        new int[] { fileNames.Length, (int)inputHeight, (int)inputWidth, 3 });
                else
                {
                    throw new Exception(String.Format("Conversion to tensor of type {0} is not implemented",
                        typeof(T)));
                }

                dataPtr = t.DataPointer;
                step = NativeImageIO.ReadBitmapToTensor<T>(bmp0, dataPtr, inputHeight, inputWidth, inputMean, scale,
                    flipUpSideDown, swapBR);
                dataPtr = new IntPtr(dataPtr.ToInt64() + step);
            }

            for (int i = 1; i < fileNames.Length; i++)
            {
                fileName = fileNames[i];
                if (!File.Exists(fileName))
                    throw new FileNotFoundException(String.Format("File {0} do not exist.", fileName));

                //Read the file using Bitmap class
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(fileName))
                {
                    step = NativeImageIO.ReadBitmapToTensor<T>(bmp, dataPtr, inputHeight, inputWidth, inputMean, scale,
                        flipUpSideDown, swapBR);

                    dataPtr = new IntPtr(dataPtr.ToInt64() + step);
                }

            }
#endif
            return t;
        }

        /*
        /// <summary>
        /// Read image files, covert the data and save it to the native pointer
        /// </summary>
        /// <typeparam name="T">The type of the data to covert the image pixel values to. e.g. "float" or "byte"</typeparam>
        /// <param name="fileNames">The name of the image files</param>
        /// <param name="inputHeight">The height of the image, must match the height requirement for the tensor</param>
        /// <param name="inputWidth">The width of the image, must match the width requirement for the tensor</param>
        /// <param name="inputMean">The mean value, it will be subtracted from the input image pixel values</param>
        /// <param name="scale">The scale, after mean is subtracted, the scale will be used to multiply the pixel values</param>
        /// <param name="flipUpSideDown">If true, the image needs to be flipped up side down</param>
        /// <param name="swapBR">If true, will flip the Blue channel with the Red. e.g. If false, the tensor's color channel order will be RGB. If true, the tensor's color channle order will be BGR </param>
        /// <returns>The tensor that contains all the image files</returns>
        private static Tensor ReadImageFilesToTensor<T>(
            String[] fileNames,
            int inputHeight = -1,
            int inputWidth = -1,
            float inputMean = 0.0f,
            float scale = 1.0f,
            bool flipUpSideDown = false,
            bool swapBR = false)
            where T : struct
        {
            if (fileNames.Length == 0)
                throw new ArgumentException("Intput file names do not contain any files");

            String fileName = fileNames[0];
            if (!File.Exists(fileName))
                throw new FileNotFoundException(String.Format("File {0} do not exist.", fileName));

            IntPtr dataPtr;
            int step;
            Tensor t;
            using (System.Drawing.Bitmap bmp0 = new Bitmap(fileName))
            {
                if (inputHeight <= 0)
                    inputHeight = bmp0.Height;
                if (inputWidth <= 0)
                    inputWidth = bmp0.Width;


                if (typeof(T) == typeof(float))
                    t = new Tensor(DataType.Float,
                        new int[] { fileNames.Length, (int)inputHeight, (int)inputWidth, 3 });
                else if (typeof(T) == typeof(byte))
                    t = new Tensor(DataType.Uint8,
                        new int[] { fileNames.Length, (int)inputHeight, (int)inputWidth, 3 });
                else
                {
                    throw new Exception(String.Format("Conversion to tensor of type {0} is not implemented",
                        typeof(T)));
                }

                dataPtr = t.DataPointer;
                step = NativeImageIO.ReadBitmapToTensor<T>(bmp0, dataPtr, inputHeight, inputWidth, inputMean, scale,
                    flipUpSideDown, swapBR);
                dataPtr = new IntPtr(dataPtr.ToInt64() + step);
            }

            for (int i = 1; i < fileNames.Length; i++)
            {
                fileName = fileNames[i];
                if (!File.Exists(fileName))
                    throw new FileNotFoundException(String.Format("File {0} do not exist.", fileName));

                //Read the file using Bitmap class
                using (System.Drawing.Bitmap bmp = new Bitmap(fileName))
                {
                    step = NativeImageIO.ReadBitmapToTensor<T>(bmp, dataPtr, inputHeight, inputWidth, inputMean, scale,
                        flipUpSideDown, swapBR);

                    dataPtr = new IntPtr(dataPtr.ToInt64() + step);
                }

            }

            return t;
        }*/
#endif
    }
}
