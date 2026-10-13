namespace Modules.SafeSiteApiService.Editor
{
    /// <summary>
    /// Implement in any editor assembly to adjust the project per headset platform (XR settings,
    /// shaders, scripting defines) around each Addressables content build. Found with TypeCache;
    /// needs a public parameterless constructor.
    /// When no hook exists, the Content Uploader builds once and uploads that output to every
    /// selected platform.
    /// </summary>
    public interface ISafeSitePlatformBuildHook
    {
        /// <param name="platform">"meta" or "pico".</param>
        void BeforeContentBuild(string platform);

        /// <param name="platform">"meta" or "pico".</param>
        void AfterContentBuild(string platform);
    }
}
