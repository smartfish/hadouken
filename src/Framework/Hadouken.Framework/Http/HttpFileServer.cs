﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Hadouken.Framework.Http.TypeScript;

namespace Hadouken.Framework.Http
{
    public class HttpFileServer : IHttpFileServer
    {
        private readonly HttpListener _httpListener = new HttpListener();
        private readonly ITypeScriptCompiler _typeScriptCompiler = TypeScriptCompiler.Create();
        private readonly string _baseDirectory;
        private readonly string _uriPrefix;

        private readonly CancellationTokenSource _cancellationToken = new CancellationTokenSource();
        private readonly Task _workerTask;

        private static readonly IDictionary<string, string> MimeTypes = new Dictionary<string, string>()
        {
            {".html", "text/html"},
            {".css", "text/css"},
            {".js", "text/javascript"},
            {".woff", "application/font-woff"},
            {".ttf", "application/x-font-ttf"},
            {".svg", "application/octet-stream"}
        };

        public HttpFileServer(string listenUri, string baseDirectory)
            : this(listenUri, baseDirectory, String.Empty)
        {
        }

        public HttpFileServer(string listenUri, string baseDirectory, string uriPrefix)
        {
            _baseDirectory = baseDirectory;
            _uriPrefix = uriPrefix;
            _httpListener.Prefixes.Add(listenUri);

            _workerTask = new Task(ct => Run(_cancellationToken.Token), _cancellationToken.Token);
        }

        public void Open()
        {
            _workerTask.Start();
        }

        public void Close()
        {
            _cancellationToken.Cancel();
            _workerTask.Wait();
        }

        private async void Run(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _httpListener.Start();

            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await _httpListener.GetContextAsync();

                if (cancellationToken.IsCancellationRequested)
                    break;

                ProcessContext(context);
            }

            _httpListener.Close();
        }

        private void ProcessContext(HttpListenerContext context)
        {
            var requestedPath = context.Request.Url.AbsolutePath == "/"
                ? "/index.html"
                : context.Request.Url.AbsolutePath;

            var path = _baseDirectory +
                       requestedPath.Substring(_uriPrefix.EndsWith("/")
                           ? _uriPrefix.Length - 1
                           : _uriPrefix.Length);

            var extension = Path.GetExtension(path);

            if (File.Exists(path))
            {
                switch (extension)
                {
                    case ".ts":
                        CompileTypeScript(context, path);
                        break;

                    default:
                        ReturnFileContent(context, path);
                        break;
                }
            }
            else
            {
                context.Response.StatusCode = 404;

                using (var writer = new StreamWriter(context.Response.OutputStream))
                {
                    writer.Write("404 - Not found");
                }
            }

            context.Response.OutputStream.Close();
            context.Response.Close();
        }

        private void CompileTypeScript(HttpListenerContext context, string path)
        {
            var typescriptFile = _typeScriptCompiler.Compile(path);

            ReturnFileContent(context, typescriptFile);
        }

        private void ReturnFileContent(HttpListenerContext context, string path)
        {
            var extension = Path.GetExtension(path);

            context.Response.ContentType = MimeTypes.ContainsKey(extension) ? MimeTypes[extension] : "text/plain";
            context.Response.StatusCode = 200;

            using (var reader = new StreamReader(path))
            using (var writer = new StreamWriter(context.Response.OutputStream))
            {
                writer.Write(reader.ReadToEnd());
            }
        }
    }
}