﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.DocAsCode.Build.Engine
{
    using System;
    using System.IO;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.DocAsCode.Common;

    public class XRefMapDownloader
    {
        private readonly SemaphoreSlim _semaphore;

        public XRefMapDownloader(int maxParallelism = 0x10)
        {
            _semaphore = new SemaphoreSlim(maxParallelism);
        }

        /// <summary>
        /// Download xref map file from uri (async).
        /// </summary>
        /// <param name="uri">The uri of xref map file.</param>
        /// <returns>An instance of <see cref="XRefMap"/>.</returns>
        /// <threadsafety>This method is thread safe.</threadsafety>
        public async Task<XRefMap> DownloadAsync(Uri uri)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(nameof(uri));
            }
            if (!uri.IsAbsoluteUri)
            {
                throw new ArgumentException("Relative path is not allowed.", nameof(uri));
            }
            await _semaphore.WaitAsync();
            return await Task.Run(async () =>
            {
                try
                {
                    return await DownloadBySchemeAsync(uri);
                }
                finally
                {
                    _semaphore.Release();
                }
            });
        }

        /// <remarks>
        /// Support scheme: http, https, ftp, file, embedded.
        /// </remarks>
        protected virtual async Task<XRefMap> DownloadBySchemeAsync(Uri uri)
        {
            XRefMap result = null;
            if (uri.IsFile)
            {
                result = DownloadFromLocal(uri);
            }
            else if (uri.Scheme == Uri.UriSchemeHttp ||
                uri.Scheme == Uri.UriSchemeHttps ||
                uri.Scheme == Uri.UriSchemeFtp)
            {
                result = await DownloadFromWebAsync(uri);
            }
            else if (uri.Scheme == "embedded")
            {
                result = DownloadFromAssembly(uri);
            }
            else
            {
                throw new ArgumentException($"Unsupported scheme {uri.Scheme}, expected: http, https, ftp, file, embedded.", nameof(uri));
            }
            if (result == null)
            {
                throw new InvalidDataException($"Invalid yaml file from {uri}.");
            }
            return result;
        }

        protected static XRefMap DownloadFromLocal(Uri uri)
        {
            using (var sr = File.OpenText(uri.LocalPath))
            {
                return YamlUtility.Deserialize<XRefMap>(sr);
            }
        }

        protected static async Task<XRefMap> DownloadFromWebAsync(Uri uri)
        {
            using (var wc = new WebClient())
            using (var stream = await wc.OpenReadTaskAsync(uri))
            using (var sr = new StreamReader(stream))
            {
                return YamlUtility.Deserialize<XRefMap>(sr);
            }
        }

        private XRefMap DownloadFromAssembly(Uri uri)
        {
            var path = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
            var index = path.IndexOf('/');
            if (index == -1)
            {
                throw new ArgumentException($"Invalid uri {uri.OriginalString}, expect: {uri.Scheme}:{{assemblyName}}/{{resourceName}}", nameof(uri));
            }
            var assemblyName = path.Remove(index);
            var resourceName = path.Substring(index + 1);

            var assembly = AppDomain.CurrentDomain.Load(assemblyName);
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            using (var sr = new StreamReader(stream))
            {
                return YamlUtility.Deserialize<XRefMap>(sr);
            }
        }

        public static void UpdateHref(XRefMap map, Uri uri)
        {
            if (!string.IsNullOrEmpty(map.BaseUrl))
            {
                Uri baseUri;
                if (!Uri.TryCreate(map.BaseUrl, UriKind.Absolute, out baseUri))
                {
                    throw new InvalidDataException($"Xref map file (from {uri.AbsoluteUri}) has an invalid base url: {map.BaseUrl}.");
                }
                map.UpdateHref(baseUri);
                return;
            }
            if (uri.Scheme == "http" || uri.Scheme == "https")
            {
                map.UpdateHref(uri);
                return;
            }
            throw new InvalidDataException($"Xref map file (from {uri.AbsoluteUri}) missing base url.");
        }
    }
}
