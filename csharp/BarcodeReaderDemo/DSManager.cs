using System;
using System.Collections.Generic;

using DirectShowLib;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Drawing;

namespace Barcode_Reader_Demo
{
    class DSManager : ISampleGrabberCB
    {
        private int _previewWidth = 640;
        private int _previewHeight = 480;
        private int _previewStride = 0;
        private int _previewFPS = 30;

        IVideoWindow videoWindow = null;
        IMediaControl mediaControl = null;
        IFilterGraph2 graphBuilder = null;
        ICaptureGraphBuilder2 captureGraphBuilder = null;

        DsROTEntry rot = null;
        IntPtr owner;
        List<CameraInfo> cameras;
        private int windowWidth, windowHeight;

        public DSManager (TaskCompletedCallBack callback, IntPtr owner, int windowWidth, int windowHeight)
        {
            this.callback = callback;
            this.owner = owner;
            this.windowWidth = windowWidth;
            this.windowHeight = windowHeight;
            DsDevice[] devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            if (devices != null)
            {
                cameras = new List<CameraInfo>();
                foreach (DsDevice device in devices)
                {
                    List<Resolution> resolutions = GetAllAvailableResolution(device);
                    cameras.Add(new CameraInfo(device, resolutions));
                }
            }
            else
            {
                throw new Exception("No device detected!");
            }
        }

        public struct Resolution
        {
            public Resolution(int width, int height)
            {
                Width = width;
                Height = height;
            }

            public int Width { get; }
            public int Height { get; }

            public override string ToString() => $"({Width} x {Height})";
        }

        public struct CameraInfo
        {
            public CameraInfo(DsDevice device, List<Resolution> resolutions)
            {
                Device = device;
                Resolutions = resolutions;
            }

            public DsDevice Device { get; }
            public List<Resolution> Resolutions { get; }
        }

        // https://stackoverflow.com/questions/20414099/videocamera-get-supported-resolutions
        private List<Resolution> GetAllAvailableResolution(DsDevice vidDev)
        {
            try
            {
                int hr, bitCount = 0;

                IBaseFilter sourceFilter = null;

                var m_FilterGraph2 = new FilterGraph() as IFilterGraph2;
                hr = m_FilterGraph2.AddSourceFilterForMoniker(vidDev.Mon, null, vidDev.Name, out sourceFilter);
                var pRaw2 = DsFindPin.ByCategory(sourceFilter, PinCategory.Capture, 0);
                var AvailableResolutions = new List<Resolution>();

                VideoInfoHeader v = new VideoInfoHeader();
                IEnumMediaTypes mediaTypeEnum;
                hr = pRaw2.EnumMediaTypes(out mediaTypeEnum);

                AMMediaType[] mediaTypes = new AMMediaType[1];
                IntPtr fetched = IntPtr.Zero;
                hr = mediaTypeEnum.Next(1, mediaTypes, fetched);

                while (fetched != null && mediaTypes[0] != null)
                {
                    Marshal.PtrToStructure(mediaTypes[0].formatPtr, v);
                    if (v.BmiHeader.Size != 0 && v.BmiHeader.BitCount != 0)
                    {
                        if (v.BmiHeader.BitCount > bitCount)
                        {
                            AvailableResolutions.Clear();
                            bitCount = v.BmiHeader.BitCount;
                        }
                        AvailableResolutions.Add(new Resolution(v.BmiHeader.Width, v.BmiHeader.Height));
                    }
                    hr = mediaTypeEnum.Next(1, mediaTypes, fetched);
                }
                return AvailableResolutions;
            }
            catch (Exception ex)
            {
                //MessageBox.Show(ex.Message);
                Console.WriteLine(ex.ToString());
                return new List<Resolution>();
            }
        }

        public List<CameraInfo> GetCameras()
        {
            return cameras;
        }

        public void StartCamera(int cameraIndex, int resolutionIndex)
        {
            if (cameras.Count == 0) throw new Exception("No device detected!");
            if (cameraIndex < 0) cameraIndex = 0;
            if (resolutionIndex < 0) resolutionIndex = 0;
            CaptureVideo(cameras[cameraIndex].Device, cameras[cameraIndex].Resolutions[resolutionIndex]);
        }

        public void StopCamera()
        {
            CloseInterfaces();
        }

        public void CaptureVideo(DsDevice device, Resolution resolution)
        {
            int hr = 0;
            IBaseFilter sourceFilter = null;
            ISampleGrabber sampleGrabber = null;

            try
            {
                // Get DirectShow interfaces
                GetInterfaces();

                // Attach the filter graph to the capture graph
                hr = this.captureGraphBuilder.SetFiltergraph(this.graphBuilder);
                DsError.ThrowExceptionForHR(hr);

                // Use the system device enumerator and class enumerator to find
                // a video capture/preview device, such as a desktop USB video camera.
                sourceFilter = SelectCaptureDevice(device);

                // Get and set camera focus
                IAMCameraControl aMCameraControl = (IAMCameraControl)sourceFilter;
                if (aMCameraControl == null) throw new ApplicationException("Cannot get IAMCameraControl");
                int iValue = 0;
                CameraControlFlags flag;
                aMCameraControl.Get(CameraControlProperty.Focus, out iValue, out flag);
                int min, max, pDelta, pDefault;
                aMCameraControl.GetRange(CameraControlProperty.Focus, out min, out max, out pDelta, out pDefault, out flag);
                aMCameraControl.Set(CameraControlProperty.Focus, 100, CameraControlFlags.Auto);

                // Add Capture filter to graph.
                hr = this.graphBuilder.AddFilter(sourceFilter, "Video Capture");
                DsError.ThrowExceptionForHR(hr);

                // Initialize SampleGrabber.
                sampleGrabber = new SampleGrabber() as ISampleGrabber;
                // Configure SampleGrabber. Add preview callback.
                ConfigureSampleGrabber(sampleGrabber);
                // Add SampleGrabber to graph.
                hr = this.graphBuilder.AddFilter(sampleGrabber as IBaseFilter, "Frame Callback");
                DsError.ThrowExceptionForHR(hr);

                // Configure preview settings.
                _previewWidth = resolution.Width;
                _previewHeight = resolution.Height;
                SetConfigParams(this.captureGraphBuilder, sourceFilter, _previewFPS, _previewWidth, _previewHeight);

                // Render the preview
                hr = this.captureGraphBuilder.RenderStream(PinCategory.Preview, MediaType.Video, sourceFilter, (sampleGrabber as IBaseFilter), null);
                DsError.ThrowExceptionForHR(hr);

                SaveSizeInfo(sampleGrabber);

                // Set video window style and position
                SetupVideoWindow();

                // Add our graph to the running object table, which will allow
                // the GraphEdit application to "spy" on our graph
                rot = new DsROTEntry(this.graphBuilder);

                // Start previewing video data
                hr = this.mediaControl.Run();
                DsError.ThrowExceptionForHR(hr);
            }
            catch
            {
                Console.WriteLine("An unrecoverable error has occurred.");
            }
            finally
            {
                if (sourceFilter != null)
                {
                    Marshal.ReleaseComObject(sourceFilter);
                    sourceFilter = null;
                }

                if (sampleGrabber != null)
                {
                    Marshal.ReleaseComObject(sampleGrabber);
                    sampleGrabber = null;
                }
            }
        }

        public IBaseFilter SelectCaptureDevice(DsDevice device)
        {
            object source = null;
            Guid iid = typeof(IBaseFilter).GUID;
            device.Mon.BindToObject(null, null, ref iid, out source);
            return (IBaseFilter)source;
        }

        public IBaseFilter FindCaptureDevice()
        {
            int hr = 0;
#if USING_NET11
      UCOMIEnumMoniker classEnum = null;
      UCOMIMoniker[] moniker = new UCOMIMoniker[1];
#else
            IEnumMoniker classEnum = null;
            IMoniker[] moniker = new IMoniker[1];
#endif
            object source = null;

            // Create the system device enumerator
            ICreateDevEnum devEnum = (ICreateDevEnum)new CreateDevEnum();

            // Create an enumerator for the video capture devices
            hr = devEnum.CreateClassEnumerator(FilterCategory.VideoInputDevice, out classEnum, 0);
            DsError.ThrowExceptionForHR(hr);

            // The device enumerator is no more needed
            Marshal.ReleaseComObject(devEnum);

            // If there are no enumerators for the requested type, then 
            // CreateClassEnumerator will succeed, but classEnum will be NULL.
            if (classEnum == null)
            {
                throw new ApplicationException("No video capture device was detected.\r\n\r\n" +
                                               "This sample requires a video capture device, such as a USB WebCam,\r\n" +
                                               "to be installed and working properly.  The sample will now close.");
            }

            // Use the first video capture device on the device list.
            // Note that if the Next() call succeeds but there are no monikers,
            // it will return 1 (S_FALSE) (which is not a failure).  Therefore, we
            // check that the return code is 0 (S_OK).
#if USING_NET11
      int i;
      if (classEnum.Next (moniker.Length, moniker, IntPtr.Zero) == 0)
#else
            if (classEnum.Next(moniker.Length, moniker, IntPtr.Zero) == 0)
#endif
            {
                // Bind Moniker to a filter object
                Guid iid = typeof(IBaseFilter).GUID;
                moniker[0].BindToObject(null, null, ref iid, out source);
            }
            else
            {
                throw new ApplicationException("Unable to access video capture device!");
            }

            // Release COM objects
            Marshal.ReleaseComObject(moniker[0]);
            Marshal.ReleaseComObject(classEnum);

            // An exception is thrown if cast fail
            return (IBaseFilter)source;
        }

        public void GetInterfaces()
        {
            int hr = 0;

            // An exception is thrown if cast fail
            this.graphBuilder = (IFilterGraph2)new FilterGraph();
            this.captureGraphBuilder = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
            this.mediaControl = (IMediaControl)this.graphBuilder;
            this.videoWindow = (IVideoWindow)this.graphBuilder;
            DsError.ThrowExceptionForHR(hr);
        }

        public void CloseInterfaces()
        {
            if (mediaControl != null)
            {
                int hr = mediaControl.StopWhenReady();
                DsError.ThrowExceptionForHR(hr);
            }

            if (videoWindow != null)
            {
                videoWindow.put_Visible(OABool.False);
                videoWindow.put_Owner(IntPtr.Zero);
            }

            // Remove filter graph from the running object table.
            if (rot != null)
            {
                rot.Dispose();
                rot = null;
            }

            // Release DirectShow interfaces.
            if (mediaControl != null)
            {
                Marshal.ReleaseComObject(mediaControl);
                mediaControl = null;
            }

            if (videoWindow != null)
            {
                Marshal.ReleaseComObject(videoWindow);
                videoWindow = null;
            }

            if (graphBuilder != null)
            {
                Marshal.ReleaseComObject(graphBuilder);
                graphBuilder = null;
            }

            if (captureGraphBuilder != null)
            {
                Marshal.ReleaseComObject(captureGraphBuilder);
                captureGraphBuilder = null;
            }
        }

        public void SetupVideoWindow()
        {
            int hr = 0;

            // Set the video window to be a child of the PictureBox
            hr = this.videoWindow.put_Owner(owner);
            DsError.ThrowExceptionForHR(hr);

            hr = this.videoWindow.put_WindowStyle(WindowStyle.Child);
            DsError.ThrowExceptionForHR(hr);

            // Make the video window visible, now that it is properly positioned
            hr = this.videoWindow.put_Visible(OABool.True);
            DsError.ThrowExceptionForHR(hr);

            // Set the video position
            int top, width, height;
            double xRatio = _previewWidth * 1.0 / windowWidth;
            double yRatio = _previewHeight * 1.0 / windowHeight;
            double ratio = xRatio > yRatio ? xRatio : yRatio;
            width = (int)(_previewWidth * 1.0 / ratio);
            height = (int)(_previewHeight * 1.0 / ratio);
            top = (windowHeight - height) / 2;

            hr = videoWindow.SetWindowPosition(0, top, width, height);
            DsError.ThrowExceptionForHR(hr);
        }

        public int SampleCB(double SampleTime, IMediaSample pSample)
        {
            throw new NotImplementedException();
        }

        private void SetConfigParams(ICaptureGraphBuilder2 capGraph, IBaseFilter capFilter, int iFrameRate, int iWidth, int iHeight)
        {
            int hr;
            object config;
            AMMediaType mediaType;
            // Find the stream config interface
            hr = capGraph.FindInterface(
                PinCategory.Capture, MediaType.Video, capFilter, typeof(IAMStreamConfig).GUID, out config);

            IAMStreamConfig videoStreamConfig = config as IAMStreamConfig;
            if (videoStreamConfig == null)
            {
                throw new Exception("Failed to get IAMStreamConfig");
            }

            // Get the existing format block
            hr = videoStreamConfig.GetFormat(out mediaType);
            DsError.ThrowExceptionForHR(hr);

            // copy out the videoinfoheader
            VideoInfoHeader videoInfoHeader = new VideoInfoHeader();
            Marshal.PtrToStructure(mediaType.formatPtr, videoInfoHeader);

            // if overriding the framerate, set the frame rate
            if (iFrameRate > 0)
            {
                videoInfoHeader.AvgTimePerFrame = 10000000 / iFrameRate;
            }

            // if overriding the width, set the width
            if (iWidth > 0)
            {
                videoInfoHeader.BmiHeader.Width = iWidth;
            }

            // if overriding the Height, set the Height
            if (iHeight > 0)
            {
                videoInfoHeader.BmiHeader.Height = iHeight;
            }

            // Copy the media structure back
            Marshal.StructureToPtr(videoInfoHeader, mediaType.formatPtr, false);

            // Set the new format
            hr = videoStreamConfig.SetFormat(mediaType);
            DsError.ThrowExceptionForHR(hr);

            DsUtils.FreeAMMediaType(mediaType);
            mediaType = null;
        }

        private void SaveSizeInfo(ISampleGrabber sampleGrabber)
        {
            int hr;

            // Get the media type from the SampleGrabber
            AMMediaType media = new AMMediaType();
            hr = sampleGrabber.GetConnectedMediaType(media);
            DsError.ThrowExceptionForHR(hr);

            if ((media.formatType != FormatType.VideoInfo) || (media.formatPtr == IntPtr.Zero))
            {
                throw new NotSupportedException("Unknown Grabber Media Format");
            }

            // Grab the size info
            VideoInfoHeader videoInfoHeader = (VideoInfoHeader)Marshal.PtrToStructure(media.formatPtr, typeof(VideoInfoHeader));
            _previewStride = _previewWidth * (videoInfoHeader.BmiHeader.BitCount / 8);

            DsUtils.FreeAMMediaType(media);
            media = null;
        }

        private void ConfigureSampleGrabber(ISampleGrabber sampleGrabber)
        {
            AMMediaType media;
            int hr;

            // Set the media type to Video/RBG24
            media = new AMMediaType();
            media.majorType = MediaType.Video;
            media.subType = MediaSubType.RGB24;
            media.formatType = FormatType.VideoInfo;

            hr = sampleGrabber.SetMediaType(media);
            DsError.ThrowExceptionForHR(hr);

            DsUtils.FreeAMMediaType(media);
            media = null;

            hr = sampleGrabber.SetCallback(this, 1);
            DsError.ThrowExceptionForHR(hr);
        }

        public int BufferCB(double SampleTime, IntPtr pBuffer, int BufferLen)
        {
            Bitmap bitmap = new Bitmap(_previewWidth, _previewHeight, _previewStride,
                PixelFormat.Format24bppRgb, pBuffer);
            bitmap.RotateFlip(RotateFlipType.Rotate180FlipX);
            if (callback != null)
            {
                callback(bitmap);
            }
            
            return 0;
        }

        public delegate void TaskCompletedCallBack(Bitmap bitmap);
        TaskCompletedCallBack callback;
    }
}
