using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web;
using System.Web.Optimization;
using NUglify;
using NUglify.JavaScript;

namespace AspNetBundling
{
    /// <summary>
    /// Represents a custom AjaxMin bundle builder for bundling from individual file contents.
    /// Generates source map files for JS.
    /// </summary>
    public class ScriptWithSourceMapBundleBuilder : IBundleBuilder
    {
        public string BuildBundleContent(Bundle bundle, BundleContext context, IEnumerable<BundleFile> files)
        {
            if (files == null)
            {
                return string.Empty;
            }
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            if (bundle == null)
            {
                throw new ArgumentNullException("bundle");
            }

            // Generates source map using an approach documented here: http://ajaxmin.codeplex.com/discussions/446616
            var sourcePath = VirtualPathUtility.ToAbsolute(bundle.Path);
            var mapVirtualPath = string.Concat(bundle.Path, "map"); // don't use .map so it's picked up by the bundle module
            var mapPath = VirtualPathUtility.ToAbsolute(mapVirtualPath);

            // Concatenate file contents to be minified, including the sourcemap hints
            var contentConcatedString = GetContentConcated(context, files);

            // Try minify (+ source map) using AjaxMin dll
            try
            {
                var contentBuilder = new StringBuilder();
                var mapBuilder = new StringBuilder();
                using (var contentWriter = new StringWriter(contentBuilder))
                using (var mapWriter = new StringWriter(mapBuilder))
                using (var sourceMap = new V3SourceMap(mapWriter))
                {
                    var settings = new CodeSettings()
                    {
                        EvalTreatment = EvalTreatment.MakeImmediateSafe,
                        PreserveImportantComments = preserveImportantComments,
                        SymbolsMap = sourceMap,
                        TermSemicolons = true,
                        MinifyCode = minifyCode
                    };

                    sourceMap.StartPackage(sourcePath, mapPath);

                    var result = Uglify.Js(contentConcatedString, settings);
                    string contentMinified = result.Code;
                    if (result.Errors.Count > 0)
                    {
                        return GenerateMinifierErrorsContent(contentConcatedString, result);
                    }

                    contentWriter.Write(contentMinified);
                }

                // Write the SourceMap to another Bundle
                AddContentToAdHocBundle(context, mapVirtualPath, mapBuilder.ToString());

                return contentBuilder.ToString();
            }
            catch (Exception ex)
            {
                // only Trace the fact that an exception occurred to the Warning output, but use Informational tracing for added detail for diagnosis
                Trace.TraceWarning("An exception occurred trying to build bundle contents for bundle with virtual path: " + bundle.Path + ". See Exception details in Information.");
                
                string bundlePrefix = "[Bundle '" + bundle.Path + "']";
                Trace.TraceInformation(bundlePrefix + " exception message: " + ex.Message);
                
                if (ex.InnerException != null && !string.IsNullOrEmpty(ex.InnerException.Message))
                {
                    Trace.TraceInformation(bundlePrefix + " inner exception message: " + ex.InnerException.Message);
                }
                
                Trace.TraceInformation(bundlePrefix + " source: " + ex.Source);
                Trace.TraceInformation(bundlePrefix + " stack trace: " + ex.StackTrace);
                
                return GenerateGenericErrorsContent(contentConcatedString);
            }
        }

        private static string GenerateGenericErrorsContent(string contentConcatedString)
        {
            var sbContent = new StringBuilder();
            sbContent.Append("/* ");
            sbContent.Append("An error occurred during minification, see Trace log for more details - returning concatenated content unminified.").Append("\r\n");
            sbContent.Append(" */\r\n");
            sbContent.Append(contentConcatedString);
            return sbContent.ToString();
        }

        private static string GenerateMinifierErrorsContent(string contentConcatedString, UglifyResult result)
        {
            var sbContent = new StringBuilder();
            sbContent.Append("/* ");
            sbContent.Append("An error occurred during minification, see errors below - returning concatenated content unminified.").Append("\r\n");
            foreach (var error in result.Errors)
            {
                sbContent.Append(error).Append("\r\n");
            }
            sbContent.Append(" */\r\n");
            sbContent.Append(contentConcatedString);
            return sbContent.ToString();
        }

        private static string GetContentConcated(BundleContext context, IEnumerable<BundleFile> files)
        {
            var contentConcated = new StringBuilder();

            foreach (var file in files)
            {
                // Get the contents of the bundle,
                // noting it may have transforms applied that could mess with any source mapping we want to do
                var contents = file.ApplyTransforms();

                // If there were transforms that were applied
                if (file.Transforms.Count > 0)
                {
                    // Write the transformed contents to another Bundle
                    var fileVirtualPath = file.IncludedVirtualPath;
                    var virtualPathTransformed = "~/" + Path.ChangeExtension(fileVirtualPath, string.Concat(".transformed",  Path.GetExtension(fileVirtualPath)));
                    AddContentToAdHocBundle(context, virtualPathTransformed, contents);
                }

                contentConcated.AppendLine(contents);
            }

            return contentConcated.ToString();
        }

        private static void AddContentToAdHocBundle(BundleContext context, string virtualPath, string content)
        {
            var mapBundle = context.BundleCollection.GetBundleFor(virtualPath);
            if (mapBundle == null)
            {
                mapBundle = new AdHocBundle(virtualPath);
                context.BundleCollection.Add(mapBundle);
            }
            var correctlyCastMapBundle = mapBundle as AdHocBundle;
            if (correctlyCastMapBundle == null)
            {
                throw new InvalidOperationException(string.Format("There is a bundle on the VirtualPath '{0}' of the type '{1}' when it was expected to be of the type 'SourceMapBundle'. That Virtual Path is reserved for the SourceMaps.", virtualPath, mapBundle.GetType()));
            }

            correctlyCastMapBundle.SetContent(content);
        }

        internal bool minifyCode = false;

        internal bool preserveImportantComments = true;
    }
}
