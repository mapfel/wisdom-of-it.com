module Build

open System
open System.Diagnostics
open System.Formats.Tar
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open Fake.Core
open Fake.Core.TargetOperators
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open FsReveal

// The reveal.js release (npm package) the slides are built with
let revealJsVersion = "6.0.2"
let revealJsSha1 = "4249dcbb9b98417c341b9b8af4ddee91e152b0e4"

let rootDir = Path.GetFullPath(__SOURCE_DIRECTORY__ </> ".." </> "..")
let outDir = rootDir </> "output"
let slidesDir = rootDir </> "slides"
let revealJsDir = rootDir </> "packages" </> ("reveal.js-" + revealJsVersion)

let restoreRevealJs () =
    if not (Directory.Exists(revealJsDir </> "dist")) then
        let url = sprintf "https://registry.npmjs.org/reveal.js/-/reveal.js-%s.tgz" revealJsVersion
        Trace.tracefn "Downloading %s" url
        use http = new HttpClient()
        let tgz = http.GetByteArrayAsync(url).GetAwaiter().GetResult()
        let sha1 = Convert.ToHexString(SHA1.HashData tgz)
        if not (sha1.Equals(revealJsSha1, StringComparison.OrdinalIgnoreCase)) then
            failwithf "Checksum mismatch for %s: expected %s but got %s" url revealJsSha1 sha1

        let tempDir = revealJsDir + ".tmp"
        Shell.cleanDir tempDir
        use gzip = new GZipStream(new MemoryStream(tgz), CompressionMode.Decompress)
        TarFile.ExtractToDirectory(gzip, tempDir, true)
        // npm tarballs wrap everything in a "package" folder
        Shell.deleteDir revealJsDir
        Directory.Move(tempDir </> "package", revealJsDir)
        Shell.deleteDir tempDir
    FsRevealHelper.RevealJsFolder <- revealJsDir

let copyStylesheet () =
    let stylesheet = slidesDir </> "custom.css"
    if File.Exists stylesheet then
        Directory.ensure (outDir </> "css")
        File.Copy(stylesheet, outDir </> "css" </> "custom.css", true)

let copyPics () =
    let images = slidesDir </> "images"
    if Directory.Exists images then
        Shell.copyDir (outDir </> "images") images (fun _ -> true)

let generateSlides () =
    copyPics ()
    for file in !! (slidesDir </> "**/*.md") do
        FsReveal.GenerateFromFile(file, outDir)
    copyStylesheet ()

let refreshEvent = new Event<unit>()

// Only injected by the local web server, so the published slides stay free of it
let reloadScript =
    "<script>new EventSource('/__reload').onmessage = function () { location.reload(); };</script>"

let contentType (file: string) =
    match Path.GetExtension(file).ToLowerInvariant() with
    | ".html" -> "text/html; charset=utf-8"
    | ".css" -> "text/css; charset=utf-8"
    | ".js" -> "text/javascript; charset=utf-8"
    | ".json" -> "application/json"
    | ".svg" -> "image/svg+xml"
    | ".png" -> "image/png"
    | ".jpg" | ".jpeg" -> "image/jpeg"
    | ".gif" -> "image/gif"
    | ".woff" -> "font/woff"
    | ".woff2" -> "font/woff2"
    | ".ttf" -> "font/ttf"
    | _ -> "application/octet-stream"

let handleRequest (ctx: HttpListenerContext) =
    async {
        let response = ctx.Response
        try
            try
                let path = Uri.UnescapeDataString ctx.Request.Url.AbsolutePath
                response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate")
                if path = "/__reload" then
                    // server-sent event which fires once the slides were regenerated
                    response.ContentType <- "text/event-stream"
                    let hello = Encoding.UTF8.GetBytes ": connected\n\n"
                    response.OutputStream.Write(hello, 0, hello.Length)
                    response.OutputStream.Flush()
                    do! Async.AwaitEvent refreshEvent.Publish
                    let message = Encoding.UTF8.GetBytes "data: refreshed\n\n"
                    response.OutputStream.Write(message, 0, message.Length)
                else
                    let relative = if path.EndsWith "/" then path + "index.html" else path
                    let file = Path.GetFullPath(outDir </> relative.TrimStart('/'))
                    if file.StartsWith(outDir + string Path.DirectorySeparatorChar) && File.Exists file then
                        response.ContentType <- contentType file
                        let content =
                            if Path.GetExtension(file) = ".html" then
                                File.ReadAllText(file).Replace("</body>", reloadScript + "</body>")
                                |> Encoding.UTF8.GetBytes
                            else
                                File.ReadAllBytes file
                        response.OutputStream.Write(content, 0, content.Length)
                    else
                        response.StatusCode <- 404
            with _ -> () // the browser went away
        finally
            try response.Close() with _ -> ()
    }

let startWebServer () =
    let rec listen port =
        let listener = new HttpListener()
        listener.Prefixes.Add(sprintf "http://localhost:%d/" port)
        try
            listener.Start()
            listener, port
        with :? HttpListenerException when port < 8183 ->
            listen (port + 1)

    let listener, port = listen 8083

    let rec loop () =
        async {
            let! ctx = listener.GetContextAsync() |> Async.AwaitTask
            Async.Start(handleRequest ctx)
            return! loop ()
        }
    Async.Start(loop ())

    let url = sprintf "http://localhost:%d/index.html" port
    Trace.tracefn "Serving slides at %s" url
    try
        Process.Start(ProcessStartInfo(url, UseShellExecute = true)) |> ignore
    with exn -> Trace.traceImportantfn "Could not open the browser: %s" exn.Message

let initTargets () =
    Target.create "Clean" (fun _ ->
        Shell.cleanDirs [outDir]
    )

    Target.create "GenerateSlides" (fun _ ->
        restoreRevealJs ()
        generateSlides ()
    )

    Target.create "KeepRunning" (fun _ ->
        use watcher =
            !! (slidesDir </> "**/*.*")
            |> ChangeWatcher.run (fun changes ->
                for change in changes do
                    Trace.traceImportantfn "%s was changed." change.Name
                try
                    generateSlides ()
                with exn -> Trace.traceImportantfn "Could not generate slides: %s" exn.Message
                refreshEvent.Trigger())

        startWebServer ()

        Trace.traceImportant "Waiting for slide edits. Press Enter to stop."

        Console.ReadLine() |> ignore
    )

    "Clean"
      ==> "GenerateSlides"
      ==> "KeepRunning"
      |> ignore

[<EntryPoint>]
let main argv =
    // allow "build GenerateSlides" as short form of "build --target GenerateSlides"
    let args =
        match List.ofArray argv with
        | target :: rest when not (target.StartsWith "-") -> "--target" :: target :: rest
        | args -> args

    args
    |> Context.FakeExecutionContext.Create false "build.fsx"
    |> Context.RuntimeContext.Fake
    |> Context.setExecutionContext

    initTargets ()
    Target.runOrDefaultWithArguments "KeepRunning"
    0
