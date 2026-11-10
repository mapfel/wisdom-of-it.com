# Wisdom of IT

Slides for [wisdom-of-it.com](http://wisdom-of-it.com), written in Markdown and turned into a
[reveal.js][revealjs] presentation by a modernized copy of [FsReveal](https://github.com/fsprojects/FsReveal).

## Writing slides

The slides live in [slides/index.md](slides/index.md):

- the list at the top sets `title`, `description`, `author`, `theme` and `transition`
- `***` starts a new horizontal slide, `---` a new vertical slide
- lines starting with `' ` are speaker notes
- `slides/custom.css` and `slides/images` are copied next to the generated page

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

    ./build.sh                  # generate, serve on http://localhost:8083 and reload the browser on every save
    ./build.sh GenerateSlides   # only generate the static page into output/

On Windows use `build.cmd` instead of `./build.sh`.

## How it is put together

- `src/FsReveal` - the FsReveal library, ported from .NET Framework to .NET 10 and
  [FSharp.Formatting](https://github.com/fsprojects/FSharp.Formatting) 22, Markdown slides only.
  `template.html` is the page the slides are inserted into.
- `src/FsReveal.Build` - the [FAKE](https://fake.build) 6 build project with the targets used above.
  The reveal.js version is set at the top of `Build.fs`; the release is downloaded from the npm registry
  into `packages/` on the first build.

[revealjs]: https://github.com/hakimel/reveal.js/ "reveal.js | HTML presentations made easy"
