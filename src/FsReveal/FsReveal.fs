namespace FsReveal

open System.IO
open FSharp.Formatting.Markdown

module FsRevealHelper =
    /// Root of the unpacked reveal.js npm package (the folder containing `dist`)
    let mutable RevealJsFolder = __SOURCE_DIRECTORY__
    let mutable TemplateFile = Path.Combine(__SOURCE_DIRECTORY__,"template.html")
    let mutable StyleFile = Path.Combine(__SOURCE_DIRECTORY__,"fsreveal.css")

type FsReveal private() =
    static let defaultFileName (other:FileInfo) optInput =
        match optInput with
        | Some fn -> fn
        | None -> Path.ChangeExtension(other.Name, ".html")

    static let generateOutput outDir outFile presentation =
        ensureDirectory outDir
        // since reveal.js 4 everything needed at runtime lives in dist/
        let revealDist = FsRevealHelper.RevealJsFolder @@ "dist"
        if Directory.Exists revealDist |> not then
            failwithf "reveal.js not found in %s" revealDist
        printfn "Copy reveal.js files from %s to %s" revealDist (outDir @@ "dist")
        copyFiles (fun f -> f.EndsWith(".d.ts") || f.EndsWith(".mjs")) revealDist (outDir @@ "dist")
        let template = File.ReadAllText(FsRevealHelper.TemplateFile)
        printfn "Apply template : %s" FsRevealHelper.TemplateFile
        let output = Formatting.GenerateHTML template presentation
        File.WriteAllText(outDir @@ outFile, output)
        let cssDir = outDir @@ "css"
        printfn "Copy fsreveal.css style from %s to %s" FsRevealHelper.StyleFile cssDir
        ensureDirectory cssDir
        File.Copy(FsRevealHelper.StyleFile, cssDir @@ "fsreveal.css", true)

    static let getPresentationFromMarkdownLines lines =
        let md = Formatting.preprocessing lines
        let presentation = Markdown.Parse(md) |> getPresentation
        // theme files are lower case, which matters on case sensitive web servers
        { presentation with Properties = presentation.Properties |> Map.add "theme" (presentation.Properties.["theme"].ToLowerInvariant()) }

    /// Creates a presentation from a Markdown source file specified as string.
    static member GetPresentationFromMarkdown(text : string) =
        normalizeLineBreaks(text).Split('\n')
        |> getPresentationFromMarkdownLines

    /// Write the specified presentation to a specified file in the output directory.
    /// (if a file name is not specified, the default `index.html` will be used)
    static member GenerateOutput(presentation, outDir, ?outFile) =
        generateOutput outDir (defaultArg outFile "index.html") presentation

    /// Processes a presentation specified as a Markdown document.
    /// (if a file name is not specified, the name of the Markdown file with extension `html` will be used)
    static member GenerateFromFile(fileName : string, outDir, ?outFile) =
        let file = FileInfo fileName
        if not file.Exists then failwithf "%s does not exist." file.FullName
        if file.Extension <> ".md" then failwithf "%s is not a Markdown file." file.FullName
        file.FullName
        |> File.ReadAllLines
        |> getPresentationFromMarkdownLines
        |> generateOutput outDir (defaultFileName file outFile)
