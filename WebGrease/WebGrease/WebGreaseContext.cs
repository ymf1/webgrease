// --------------------------------------------------------------------------------------------------------------------
// <copyright file="WebGreaseContext.cs" company="Microsoft">
//   Copyright Microsoft Corporation, all rights reserved
// </copyright>
// ----------------------------------------------------------------------------------------------------

namespace WebGrease
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;

    using WebGrease.Activities;
    using WebGrease.Configuration;
    using WebGrease.Extensions;
    using WebGrease.Preprocessing;

    /// <summary>
    /// The web grease context.
    /// It contains all the global information necessary for all the tasks to run.
    /// Only very task specific values should be passed separately.
    /// It also contains all global functionality, like measuring, logging and caching.
    /// </summary>
    public class WebGreaseContext : IWebGreaseContext
    {
        #region Static Fields

        /// <summary>The cached content hashes</summary>
        private static readonly IDictionary<string, string> CachedContentHashes = new Dictionary<string, string>();

        /// <summary>The cached file hashes</summary>
        private static readonly IDictionary<string, Tuple<DateTime, long, string>> CachedFileHashes = new Dictionary<string, Tuple<DateTime, long, string>>();

        /// <summary>The md5 hasher</summary>
        private static readonly MD5CryptoServiceProvider Hasher = new MD5CryptoServiceProvider();

        /// <summary>The no bom utf-8 default encoding (same defaulty encoding as the .net StreamWriter.</summary>
        private static readonly UTF8Encoding DefaultEncoding = new UTF8Encoding(false, true);

        #endregion

        #region Fields

        /// <summary>Per session in memory cache of available files.</summary>
        private readonly IDictionary<string, IDictionary<string, string>> availableFiles = new Dictionary<string, IDictionary<string, string>>();

        #endregion

        #region Constructors and Destructors

        /// <summary>Initializes a new instance of the <see cref="WebGreaseContext"/> class.</summary>
        /// <param name="parentContext">The parent context.</param>
        /// <param name="configFile">The config file.</param>
        public WebGreaseContext(IWebGreaseContext parentContext, FileInfo configFile)
        {
            var configuration = new WebGreaseConfiguration(parentContext.Configuration, configFile);
            this.Initialize(
                configuration,
                parentContext.Log,
                parentContext.Cache,
                parentContext.Preprocessing,
                parentContext.SessionStartTime,
                parentContext.Measure);
        }

        /// <summary>Initializes a new instance of the <see cref="WebGreaseContext"/> class. The web grease context.</summary>
        /// <param name="configuration">The configuration</param>
        /// <param name="logInformation">The log information.</param>
        /// <param name="logWarning">The log warning.</param>
        /// <param name="logError">The log error.</param>
        /// <param name="logExtendedError">The log extended error.</param>
        public WebGreaseContext(
            WebGreaseConfiguration configuration,
            Action<string> logInformation = null,
            LogExtendedError logWarning = null,
            LogError logError = null,
            LogExtendedError logExtendedError = null)
        {
            var runStartTime = DateTimeOffset.Now;
            configuration.Validate();
            var timeMeasure = configuration.Measure ? new TimeMeasure() as ITimeMeasure : new NullTimeMeasure();
            var logManager = new LogManager(logInformation, logWarning, logError, logExtendedError);
            var cacheManager = configuration.CacheEnabled ? new CacheManager(configuration, logManager) as ICacheManager : new NullCacheManager();
            var preprocessingManager = new PreprocessingManager(configuration, logManager, timeMeasure);
            this.Initialize(configuration, logManager, cacheManager, preprocessingManager, runStartTime, timeMeasure);
        }

        #endregion

        #region Public Properties

        /// <summary>Gets the cache manager.</summary>
        public ICacheManager Cache { get; private set; }

        /// <summary>Gets the configuration.</summary>
        public WebGreaseConfiguration Configuration { get; private set; }

        /// <summary>Gets the log.</summary>
        public LogManager Log { get; private set; }

        /// <summary>Gets the measure object.</summary>
        public ITimeMeasure Measure { get; private set; }

        /// <summary>Gets the preprocessing manager.</summary>
        public PreprocessingManager Preprocessing { get; private set; }

        /// <summary>Gets the session start time.</summary>
        public DateTimeOffset SessionStartTime { get; private set; }

        #endregion

        #region Public Methods and Operators

        /// <summary>The compute content hash.</summary>
        /// <param name="content">The content.</param>
        /// <param name="encoding"> The encoding</param>
        /// <returns>The <see cref="string"/>.</returns>
        public static string ComputeContentHash(string content, Encoding encoding = null)
        {
            using (var ms = new MemoryStream())
            {
                var sw = new StreamWriter(ms, encoding ?? DefaultEncoding);
                sw.Write(content);
                sw.Flush();
                ms.Seek(0, SeekOrigin.Begin);
                return BytesToHash(Hasher.ComputeHash(ms));
            }
        }

        /// <summary>The compute file hash.</summary>
        /// <param name="filePath">The file path.</param>
        /// <returns>The <see cref="string"/>.</returns>
        public static string ComputeFileHash(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return BytesToHash(Hasher.ComputeHash(fs));
            }
        }

        /// <summary>Starts a section.</summary>
        /// <param name="idParts">The id parts.</param>
        /// <param name="sectionAction">The section action.</param>
        /// <returns>The Success</returns>
        public bool Section(string[] idParts, Func<string, bool> sectionAction)
        {
            var id = this.Measure.Start(idParts);
            try
            {
                return sectionAction(id);
            }
            finally
            {
                this.Measure.End(id);
            }
        }

        /// <summary>Starts a section.</summary>
        /// <param name="idParts">The id parts.</param>
        /// <param name="varBySettings">The var by settings.</param>
        /// <param name="skipable">If the section is skippable.</param>
        /// <param name="sectionAction">The section action.</param>
        /// <returns>The Success</returns>
        public bool Section(string[] idParts, object varBySettings, bool skipable, Func<ICacheSection, bool> sectionAction)
        {
            return this.Section(idParts, null, varBySettings, skipable, sectionAction);
        }

        /// <summary>Starts a section.</summary>
        /// <param name="idParts">The id parts.</param>
        /// <param name="varByContentItem">The var by content item.</param>
        /// <param name="skipable">If the section is skippable.</param>
        /// <param name="sectionAction">The section action.</param>
        /// <returns>The Success</returns>
        public bool Section(string[] idParts, ContentItem varByContentItem, bool skipable, Func<ICacheSection, bool> sectionAction)
        {
            return this.Section(idParts, varByContentItem, null, skipable, sectionAction);
        }

        /// <summary>Starts a section.</summary>
        /// <param name="idParts">The id parts.</param>
        /// <param name="varByContentItem">The var by content item.</param>
        /// <param name="varBySettings">The var by settings.</param>
        /// <param name="skipable">If the section is skippable.</param>
        /// <param name="sectionAction">The section action.</param>
        /// <returns>The Success</returns>
        public bool Section(string[] idParts, ContentItem varByContentItem, object varBySettings, bool skipable, Func<ICacheSection, bool> sectionAction)
        {
            return this.Section(
                idParts, 
                id =>
                {
                    var cacheSection = this.Cache.BeginSection(id, varByContentItem, varBySettings);
                    try
                    {
                        if (skipable && cacheSection.CanBeSkipped())
                        {
                            return true;
                        }

                        if (!sectionAction(cacheSection))
                        {
                            return false;
                        }

                        cacheSection.Save();
                        return true;
                    }
                    finally
                    {
                        cacheSection.EndSection();
                    }
                });
        }

        /// <summary>The clean cache.</summary>
        public void CleanCache()
        {
            CleanDirectory(this.Configuration.CacheRootPath);
        }

        /// <summary>The clean destination.</summary>
        public void CleanDestination()
        {
            CleanDirectory(this.Configuration.DestinationDirectory);
            CleanDirectory(this.Configuration.LogsDirectory);
        }

        /// <summary>The clean tools temp.</summary>
        public void CleanToolsTemp()
        {
            CleanDirectory(this.Configuration.CacheRootPath);
        }

        /// <summary>Gets the available files, only gets them once per session/context.</summary>
        /// <param name="rootDirectory">The root directory.</param>
        /// <param name="directories">The directories.</param>
        /// <param name="extensions">The extensions.</param>
        /// <param name="fileType">The file type.</param>
        /// <returns>The available files.</returns>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "Need lowercase")]
        public IDictionary<string, string> GetAvailableFiles(string rootDirectory, IList<string> directories, IList<string> extensions, FileTypes fileType)
        {
            var key = new { rootDirectory, directories, extensions, fileType }.ToJson();
            if (!this.availableFiles.ContainsKey(key))
            {
                var results = new Dictionary<string, string>();
                if (directories == null)
                {
                    return results;
                }

                foreach (var directory in directories)
                {
                    foreach (var extension in extensions)
                    {
                        results.AddRange(
                            Directory.GetFiles(directory, extension, SearchOption.AllDirectories)
                                     .Select(f => f.ToLowerInvariant())
                                     .ToDictionary(f => f.MakeRelativeToDirectory(rootDirectory), f => f));
                    }
                }

                this.availableFiles.Add(key, results);
            }

            return this.availableFiles[key];
        }

        /// <summary>The get content hash.</summary>
        /// <param name="value">The content.</param>
        /// <returns>The <see cref="string"/>.</returns>
        public string GetValueHash(string value)
        {
            if (value == null)
            {
                value = string.Empty;
            }

            this.Measure.Start(SectionIdParts.FileHash);
            try
            {
                if (!CachedContentHashes.ContainsKey(value))
                {
                    CachedContentHashes.Add(value, ComputeContentHash(value));
                }

                return CachedContentHashes[value];
            }
            finally
            {
                this.Measure.End(SectionIdParts.FileHash);
            }
        }

        /// <summary>Gets the md5 hash for the content file.</summary>
        /// <param name="contentItem">The content file.</param>
        /// <returns>The MD5 hash.</returns>
        public string GetContentItemHash(ContentItem contentItem)
        {
            return contentItem.GetContentHash(this);
        }

        /// <summary>Gets the hash for the content of the file provided in the file path.</summary>
        /// <param name="filePath">The file path.</param>
        /// <returns>The MD5 hash.</returns>
        public string GetFileHash(string filePath)
        {
            this.Measure.Start(SectionIdParts.FileHash);
            try
            {
                var fi = new FileInfo(filePath);
                if (!fi.Exists)
                {
                    throw new FileNotFoundException("Could not find the file to create a hash for", filePath);
                }

                var uniqueId = fi.FullName;
                if (!CachedFileHashes.ContainsKey(uniqueId) || CachedFileHashes[uniqueId].Item1 != fi.LastWriteTimeUtc || CachedFileHashes[uniqueId].Item2 != fi.Length)
                {
                    CachedFileHashes[uniqueId] = new Tuple<DateTime, long, string>(fi.LastWriteTimeUtc, fi.Length, ComputeFileHash(fi.FullName));
                }

                return CachedFileHashes[uniqueId].Item3;
            }
            finally
            {
                this.Measure.End(SectionIdParts.FileHash);
            }
        }

        /// <summary>The make relative.</summary>
        /// <param name="absolutePath">The absolute path.</param>
        /// <param name="relativePath">The relative path.</param>
        /// <returns>The <see cref="string"/>.</returns>
        public string MakeRelative(string absolutePath, string relativePath = null)
        {
            return string.IsNullOrWhiteSpace(relativePath)
                ? absolutePath
                : absolutePath.MakeRelativeTo(this.Configuration.ApplicationRootDirectory);
        }

        /// <summary>The make absolute to source directory.</summary>
        /// <param name="relativePath">The relative path.</param>
        /// <returns>The <see cref="string"/>.</returns>
        public string GetWorkingSourceDirectory(string relativePath)
        {
            var sourceDirectory = this.Configuration.SourceDirectory ?? string.Empty;
            var absolutePath = Path.Combine(sourceDirectory, relativePath);
            var si = new FileInfo(absolutePath);

            return (sourceDirectory.IsNullOrWhitespace() || si.FullName.StartsWith(sourceDirectory, StringComparison.OrdinalIgnoreCase))
                ? si.DirectoryName
                : sourceDirectory;
        }

        /// <summary>The touch.</summary>
        /// <param name="filePath">The file path.</param>
        public void Touch(string filePath)
        {
            File.SetLastWriteTimeUtc(filePath, this.SessionStartTime.Date);
        }

        #endregion

        #region Methods

        /// <summary>The bytes to hash.</summary>
        /// <param name="hash">The hash.</param>
        /// <returns>The <see cref="string"/>.</returns>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "MD5 Lower case")]
        private static string BytesToHash(byte[] hash)
        {
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLower(CultureInfo.InvariantCulture);
        }

        /// <summary>The clean directory.</summary>
        /// <param name="directory">The directory.</param>
        private static void CleanDirectory(string directory)
        {
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }

            Directory.CreateDirectory(directory);
        }

        /// <summary>The initialize.</summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="logManager">The log manager.</param>
        /// <param name="cacheManager">The cache manager.</param>
        /// <param name="preprocessingManager">The preprocessing manager.</param>
        /// <param name="runStartTime">The run start time.</param>
        /// <param name="timeMeasure">The time measure.</param>
        private void Initialize(
            WebGreaseConfiguration configuration,
            LogManager logManager,
            ICacheManager cacheManager,
            PreprocessingManager preprocessingManager,
            DateTimeOffset runStartTime,
            ITimeMeasure timeMeasure)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }

            // Note: Configuration needs to be set before the other ones.
            this.Configuration = configuration;
            this.Configuration.Validate();

            this.Measure = timeMeasure;

            this.Log = logManager;

            this.Cache = cacheManager;

            this.Preprocessing = preprocessingManager;

            this.SessionStartTime = runStartTime;

            this.Cache.SetContext(this);
            this.Preprocessing.SetContext(this);
        }

        #endregion
    }
}