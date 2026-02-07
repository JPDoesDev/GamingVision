using System.Runtime.InteropServices;

namespace GamingVision.Native;

internal static class Dxgi
{
    public static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    public static readonly Guid IID_IDXGIAdapter = new("2411e7e1-12ac-4ccf-bd14-9798e8534dc0");
    public static readonly Guid IID_IDXGIOutput = new("ae02eedb-c735-4690-8d52-5a8dc20213aa");
    public static readonly Guid IID_IDXGIOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
    public static readonly Guid IID_IDXGISurface = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");

    public const int DXGI_ERROR_NOT_CURRENTLY_AVAILABLE = unchecked((int)0x887A0022);
    public const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
    public const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_OUTPUT_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public RECT DesktopCoordinates;
        public int AttachedToDesktop;
        public int Rotation;
        public IntPtr Monitor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_FRAME_INFO
    {
        public long LastPresentTime;
        public long LastMouseUpdateTime;
        public uint AccumulatedFrames;
        public int RectsCoalesced;
        public int ProtectedContentMaskedOut;
        public DXGI_OUTDUPL_POINTER_POSITION PointerPosition;
        public uint TotalMetadataBufferSize;
        public uint PointerShapeBufferSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_POINTER_POSITION
    {
        public POINT Position;
        public int Visible;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_MAPPED_RECT
    {
        public int Pitch;
        public IntPtr pBits;
    }

    [ComImport]
    [Guid("aec22fb8-76f3-4639-9be0-28eb43a67a2e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIObject
    {
        [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        [PreserveSig] int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);
        [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
    }

    [ComImport]
    [Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIDevice : IDXGIObject
    {
        [PreserveSig] new int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        [PreserveSig] new int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);
        [PreserveSig] new int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        [PreserveSig] new int GetParent(ref Guid riid, out IntPtr ppParent);

        [PreserveSig] int GetAdapter(out IntPtr pAdapter);
        [PreserveSig] int CreateSurface(IntPtr pDesc, uint NumSurfaces, uint Usage, IntPtr pSharedResource, out IntPtr ppSurface);
        [PreserveSig] int QueryResourceResidency(IntPtr ppResources, IntPtr pResidencyStatus, uint NumResources);
        [PreserveSig] int SetGPUThreadPriority(int Priority);
        [PreserveSig] int GetGPUThreadPriority(out int pPriority);
    }

    [ComImport]
    [Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIAdapter : IDXGIObject
    {
        [PreserveSig] new int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        [PreserveSig] new int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);
        [PreserveSig] new int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        [PreserveSig] new int GetParent(ref Guid riid, out IntPtr ppParent);

        [PreserveSig] int EnumOutputs(uint Output, out IntPtr ppOutput);
        [PreserveSig] int GetDesc(out DXGI_ADAPTER_DESC pDesc);
        [PreserveSig] int CheckInterfaceSupport(ref Guid InterfaceName, out long pUMDVersion);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_ADAPTER_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public IntPtr DedicatedVideoMemory;
        public IntPtr DedicatedSystemMemory;
        public IntPtr SharedSystemMemory;
        public long AdapterLuid;
    }

    [ComImport]
    [Guid("ae02eedb-c735-4690-8d52-5a8dc20213aa")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIOutput
    {
        [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        [PreserveSig] int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);
        [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);

        [PreserveSig] int GetDesc(out DXGI_OUTPUT_DESC pDesc);
        [PreserveSig] int GetDisplayModeList(uint EnumFormat, uint Flags, ref uint pNumModes, IntPtr pDesc);
        [PreserveSig] int FindClosestMatchingMode(IntPtr pModeToMatch, IntPtr pClosestMatch, IntPtr pConcernedDevice);
        [PreserveSig] int WaitForVBlank();
        [PreserveSig] int TakeOwnership(IntPtr pDevice, int Exclusive);
        [PreserveSig] void ReleaseOwnership();
        [PreserveSig] int GetGammaControlCapabilities(IntPtr pGammaCaps);
        [PreserveSig] int SetGammaControl(IntPtr pArray);
        [PreserveSig] int GetGammaControl(IntPtr pArray);
        [PreserveSig] int SetDisplaySurface(IntPtr pScanoutSurface);
        [PreserveSig] int GetDisplaySurfaceData(IntPtr pDestination);
        [PreserveSig] int GetFrameStatistics(IntPtr pStats);
    }

    [ComImport]
    [Guid("00cddea8-939b-4b83-a340-a685226666cc")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIOutput1
    {
        [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        [PreserveSig] int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);
        [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);

        [PreserveSig] int GetDesc(out DXGI_OUTPUT_DESC pDesc);
        [PreserveSig] int GetDisplayModeList(uint EnumFormat, uint Flags, ref uint pNumModes, IntPtr pDesc);
        [PreserveSig] int FindClosestMatchingMode(IntPtr pModeToMatch, IntPtr pClosestMatch, IntPtr pConcernedDevice);
        [PreserveSig] int WaitForVBlank();
        [PreserveSig] int TakeOwnership(IntPtr pDevice, int Exclusive);
        [PreserveSig] void ReleaseOwnership();
        [PreserveSig] int GetGammaControlCapabilities(IntPtr pGammaCaps);
        [PreserveSig] int SetGammaControl(IntPtr pArray);
        [PreserveSig] int GetGammaControl(IntPtr pArray);
        [PreserveSig] int SetDisplaySurface(IntPtr pScanoutSurface);
        [PreserveSig] int GetDisplaySurfaceData(IntPtr pDestination);
        [PreserveSig] int GetFrameStatistics(IntPtr pStats);

        [PreserveSig] int GetDisplayModeList1(uint EnumFormat, uint Flags, ref uint pNumModes, IntPtr pDesc);
        [PreserveSig] int FindClosestMatchingMode1(IntPtr pModeToMatch, IntPtr pClosestMatch, IntPtr pConcernedDevice);
        [PreserveSig] int GetDisplaySurfaceData1(IntPtr pDestination);
        [PreserveSig] int DuplicateOutput(IntPtr pDevice, out IntPtr ppOutputDuplication);
    }

    [ComImport]
    [Guid("191cfac3-a341-470d-b26e-a864f428319c")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIOutputDuplication
    {
        [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        [PreserveSig] int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);
        [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);

        [PreserveSig] void GetDesc(IntPtr pDesc);
        [PreserveSig] int AcquireNextFrame(uint TimeoutInMilliseconds, out DXGI_OUTDUPL_FRAME_INFO pFrameInfo, out IntPtr ppDesktopResource);
        [PreserveSig] int GetFrameDirtyRects(uint DirtyRectsBufferSize, IntPtr pDirtyRectsBuffer, out uint pDirtyRectsBufferSizeRequired);
        [PreserveSig] int GetFrameMoveRects(uint MoveRectsBufferSize, IntPtr pMoveRectBuffer, out uint pMoveRectsBufferSizeRequired);
        [PreserveSig] int GetFramePointerShape(uint PointerShapeBufferSize, IntPtr pPointerShapeBuffer, out uint pPointerShapeBufferSizeRequired, IntPtr pPointerShapeInfo);
        [PreserveSig] int MapDesktopSurface(out DXGI_MAPPED_RECT pLockedRect);
        [PreserveSig] int UnMapDesktopSurface();
        [PreserveSig] int ReleaseFrame();
    }
}
