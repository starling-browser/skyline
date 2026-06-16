using Silk.NET.WebGPU;
using Skyline.Gpu;

namespace Skyline.Gpu.Tests;

[TestClass]
public unsafe class ShaderModuleTests
{
    private const string MinimalVertexShader =
        "@vertex fn vs() -> @builtin(position) vec4f { return vec4f(0.0, 0.0, 0.0, 1.0); }";

    [TestMethod]
    public void CreatesModuleFromWgslString()
    {
        using var gpu = GpuContext.CreateHeadless();
        var module = gpu.CreateShaderModuleWgsl(MinimalVertexShader);
        Assert.IsTrue(module != null);
        gpu.Api.ShaderModuleRelease(module);
    }

    [TestMethod]
    public void LabelIsOptional()
    {
        using var gpu = GpuContext.CreateHeadless();
        var module = gpu.CreateShaderModuleWgsl(MinimalVertexShader, label: "test-module");
        Assert.IsTrue(module != null);
        gpu.Api.ShaderModuleRelease(module);
    }

    [TestMethod]
    public void InvalidWgslRaisesUncapturedError()
    {
        using var gpu = GpuContext.CreateHeadless();
        var fired = false;
        gpu.UncapturedError += (_, _) => { fired = true; };

        var module = gpu.CreateShaderModuleWgsl("this is not wgsl");
        gpu.Poll(wait: false);

        if (module != null)
        {
            gpu.Api.ShaderModuleRelease(module);
        }

        Assert.IsTrue(fired, "invalid WGSL should raise an uncaptured error");
    }
}
