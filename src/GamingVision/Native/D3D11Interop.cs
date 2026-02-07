using System.Runtime.InteropServices;

namespace GamingVision.Native;

/// <summary>
/// Direct3D 11 COM interop definitions for DXGI Desktop Duplication.
/// Separate from the vtable-based D3D11 interop in WindowsCaptureService.cs.
/// </summary>
internal static class D3D11Interop
{
    public const uint D3D11_SDK_VERSION = 7;

    public static readonly Guid IID_ID3D11Device = new("db6f6ddb-ac77-4e88-8253-819df9bbf140");
    public static readonly Guid IID_ID3D11DeviceContext = new("c0bfa96c-e089-44fb-8eaf-26f8796190da");
    public static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    [Flags]
    public enum D3D11_CREATE_DEVICE_FLAG : uint
    {
        D3D11_CREATE_DEVICE_SINGLETHREADED = 0x1,
        D3D11_CREATE_DEVICE_DEBUG = 0x2,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20,
    }

    public enum D3D_DRIVER_TYPE
    {
        D3D_DRIVER_TYPE_UNKNOWN = 0,
        D3D_DRIVER_TYPE_HARDWARE = 1,
        D3D_DRIVER_TYPE_REFERENCE = 2,
        D3D_DRIVER_TYPE_NULL = 3,
        D3D_DRIVER_TYPE_SOFTWARE = 4,
        D3D_DRIVER_TYPE_WARP = 5,
    }

    public enum D3D_FEATURE_LEVEL
    {
        D3D_FEATURE_LEVEL_9_1 = 0x9100,
        D3D_FEATURE_LEVEL_9_2 = 0x9200,
        D3D_FEATURE_LEVEL_9_3 = 0x9300,
        D3D_FEATURE_LEVEL_10_0 = 0xa000,
        D3D_FEATURE_LEVEL_10_1 = 0xa100,
        D3D_FEATURE_LEVEL_11_0 = 0xb000,
        D3D_FEATURE_LEVEL_11_1 = 0xb100,
        D3D_FEATURE_LEVEL_12_0 = 0xc000,
        D3D_FEATURE_LEVEL_12_1 = 0xc100,
    }

    public enum DXGI_FORMAT
    {
        DXGI_FORMAT_UNKNOWN = 0,
        DXGI_FORMAT_B8G8R8A8_UNORM = 87,
        DXGI_FORMAT_R8G8B8A8_UNORM = 28,
    }

    public enum D3D11_USAGE
    {
        D3D11_USAGE_DEFAULT = 0,
        D3D11_USAGE_IMMUTABLE = 1,
        D3D11_USAGE_DYNAMIC = 2,
        D3D11_USAGE_STAGING = 3,
    }

    [Flags]
    public enum D3D11_CPU_ACCESS_FLAG : uint
    {
        D3D11_CPU_ACCESS_WRITE = 0x10000,
        D3D11_CPU_ACCESS_READ = 0x20000,
    }

    public enum D3D11_MAP
    {
        D3D11_MAP_READ = 1,
        D3D11_MAP_WRITE = 2,
        D3D11_MAP_READ_WRITE = 3,
        D3D11_MAP_WRITE_DISCARD = 4,
        D3D11_MAP_WRITE_NO_OVERWRITE = 5,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public DXGI_FORMAT Format;
        public DXGI_SAMPLE_DESC SampleDesc;
        public D3D11_USAGE Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SAMPLE_DESC
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_MAPPED_SUBRESOURCE
    {
        public IntPtr pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        D3D_DRIVER_TYPE DriverType,
        IntPtr Software,
        D3D11_CREATE_DEVICE_FLAG Flags,
        [MarshalAs(UnmanagedType.LPArray)] D3D_FEATURE_LEVEL[]? pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,
        out IntPtr ppDevice,
        out D3D_FEATURE_LEVEL pFeatureLevel,
        out IntPtr ppImmediateContext);

    [ComImport]
    [Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ID3D11Device
    {
        [PreserveSig] void CreateBuffer();
        [PreserveSig] void CreateTexture1D();
        [PreserveSig] int CreateTexture2D(ref D3D11_TEXTURE2D_DESC pDesc, IntPtr pInitialData, out IntPtr ppTexture2D);
    }

    [ComImport]
    [Guid("c0bfa96c-e089-44fb-8eaf-26f8796190da")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ID3D11DeviceContext
    {
        // ID3D11DeviceChild methods (slots 3-6)
        [PreserveSig] void GetDevice(out IntPtr ppDevice);
        [PreserveSig] int GetPrivateData(ref Guid guid, ref uint pDataSize, IntPtr pData);
        [PreserveSig] int SetPrivateData(ref Guid guid, uint DataSize, IntPtr pData);
        [PreserveSig] int SetPrivateDataInterface(ref Guid guid, IntPtr pData);

        // ID3D11DeviceContext methods (slots 7+)
        [PreserveSig] void VSSetConstantBuffers(uint StartSlot, uint NumBuffers, IntPtr ppConstantBuffers);
        [PreserveSig] void PSSetShaderResources(uint StartSlot, uint NumViews, IntPtr ppShaderResourceViews);
        [PreserveSig] void PSSetShader(IntPtr pPixelShader, IntPtr ppClassInstances, uint NumClassInstances);
        [PreserveSig] void PSSetSamplers(uint StartSlot, uint NumSamplers, IntPtr ppSamplers);
        [PreserveSig] void VSSetShader(IntPtr pVertexShader, IntPtr ppClassInstances, uint NumClassInstances);
        [PreserveSig] void DrawIndexed(uint IndexCount, uint StartIndexLocation, int BaseVertexLocation);
        [PreserveSig] void Draw(uint VertexCount, uint StartVertexLocation);
        [PreserveSig] int Map(IntPtr pResource, uint Subresource, D3D11_MAP MapType, uint MapFlags, out D3D11_MAPPED_SUBRESOURCE pMappedResource);
        [PreserveSig] void Unmap(IntPtr pResource, uint Subresource);
        [PreserveSig] void PSSetConstantBuffers(uint StartSlot, uint NumBuffers, IntPtr ppConstantBuffers);
        [PreserveSig] void IASetInputLayout(IntPtr pInputLayout);
        [PreserveSig] void IASetVertexBuffers(uint StartSlot, uint NumBuffers, IntPtr ppVertexBuffers, IntPtr pStrides, IntPtr pOffsets);
        [PreserveSig] void IASetIndexBuffer(IntPtr pIndexBuffer, uint Format, uint Offset);
        [PreserveSig] void DrawIndexedInstanced(uint IndexCountPerInstance, uint InstanceCount, uint StartIndexLocation, int BaseVertexLocation, uint StartInstanceLocation);
        [PreserveSig] void DrawInstanced(uint VertexCountPerInstance, uint InstanceCount, uint StartVertexLocation, uint StartInstanceLocation);
        [PreserveSig] void GSSetConstantBuffers(uint StartSlot, uint NumBuffers, IntPtr ppConstantBuffers);
        [PreserveSig] void GSSetShader(IntPtr pShader, IntPtr ppClassInstances, uint NumClassInstances);
        [PreserveSig] void IASetPrimitiveTopology(uint Topology);
        [PreserveSig] void VSSetShaderResources(uint StartSlot, uint NumViews, IntPtr ppShaderResourceViews);
        [PreserveSig] void VSSetSamplers(uint StartSlot, uint NumSamplers, IntPtr ppSamplers);
        [PreserveSig] void Begin(IntPtr pAsync);
        [PreserveSig] void End(IntPtr pAsync);
        [PreserveSig] int GetData(IntPtr pAsync, IntPtr pData, uint DataSize, uint GetDataFlags);
        [PreserveSig] void SetPredication(IntPtr pPredicate, int PredicateValue);
        [PreserveSig] void GSSetShaderResources(uint StartSlot, uint NumViews, IntPtr ppShaderResourceViews);
        [PreserveSig] void GSSetSamplers(uint StartSlot, uint NumSamplers, IntPtr ppSamplers);
        [PreserveSig] void OMSetRenderTargets(uint NumViews, IntPtr ppRenderTargetViews, IntPtr pDepthStencilView);
        [PreserveSig] void OMSetRenderTargetsAndUnorderedAccessViews(uint NumRTVs, IntPtr ppRenderTargetViews, IntPtr pDepthStencilView, uint UAVStartSlot, uint NumUAVs, IntPtr ppUnorderedAccessViews, IntPtr pUAVInitialCounts);
        [PreserveSig] void OMSetBlendState(IntPtr pBlendState, IntPtr BlendFactor, uint SampleMask);
        [PreserveSig] void OMSetDepthStencilState(IntPtr pDepthStencilState, uint StencilRef);
        [PreserveSig] void SOSetTargets(uint NumBuffers, IntPtr ppSOTargets, IntPtr pOffsets);
        [PreserveSig] void DrawAuto();
        [PreserveSig] void DrawIndexedInstancedIndirect(IntPtr pBufferForArgs, uint AlignedByteOffsetForArgs);
        [PreserveSig] void DrawInstancedIndirect(IntPtr pBufferForArgs, uint AlignedByteOffsetForArgs);
        [PreserveSig] void Dispatch(uint ThreadGroupCountX, uint ThreadGroupCountY, uint ThreadGroupCountZ);
        [PreserveSig] void DispatchIndirect(IntPtr pBufferForArgs, uint AlignedByteOffsetForArgs);
        [PreserveSig] void RSSetState(IntPtr pRasterizerState);
        [PreserveSig] void RSSetViewports(uint NumViewports, IntPtr pViewports);
        [PreserveSig] void RSSetScissorRects(uint NumRects, IntPtr pRects);
        [PreserveSig] void CopySubresourceRegion(IntPtr pDstResource, uint DstSubresource, uint DstX, uint DstY, uint DstZ, IntPtr pSrcResource, uint SrcSubresource, IntPtr pSrcBox);
        [PreserveSig] void CopyResource(IntPtr pDstResource, IntPtr pSrcResource);
    }
}
