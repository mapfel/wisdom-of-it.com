[<AutoOpen>]
module internal FsReveal.Markdown

open System
open FSharp.Formatting.Markdown

let private isRule c paragraph =
    match paragraph with
    | HorizontalRule(character, _) -> character = c
    | _ -> false

let getPresentation (doc : MarkdownDocument) =
    /// get properties, a list of (key,value) from
    /// [[Span[Literal "key : value"]]]
    let getProperties (items : MarkdownParagraphs list) =
        let extractProperty paragraphs =
            match paragraphs with
            | [ Span([ Literal(v, _) ], _) ] when v.Contains(":") ->
                let colonPos = v.IndexOf(':')
                let key = v.Substring(0, colonPos).Trim()
                let value = v.Substring(colonPos + 1).Trim()
                (key, value)
            | _ -> failwithf "Invalid Presentation property: %A" paragraphs
        items |> List.map extractProperty

    // main section is separated by ***
    let sections = splitBy (isRule '*') doc.Paragraphs

    let properties,slideData =
        let map,slideData =
            match sections with
            | [ ListBlock(_, items, _) ] :: tail -> getProperties items |> Map.ofList,tail
            | _ -> Map.empty,sections

        let add key v map =
            match Map.tryFind key map with
            | None -> Map.add key v map
            | _ -> map

        let properties =
            map
            |> add "title" "Presentation"
            |> add "description" ""
            |> add "author" "unkown"
            |> add "theme" "night"
            |> add "transition" "default"
        properties,slideData

    let wrappedInSection (properties:Map<_,_>) paragraphs =
        let attributes = properties |> Seq.map (fun kv -> sprintf "%s=\"%s\"" kv.Key kv.Value)
        InlineHtmlBlock(sprintf "<section %s>" (String.Join(" ", attributes)), None, MarkdownRange.zero)
        :: paragraphs @ [ InlineHtmlBlock("</section>", None, MarkdownRange.zero) ]

    let getParagraphsFromSlide slide =
        match slide with
        | Simple(slideData)
        | Nested([slideData]) -> wrappedInSection slideData.Properties slideData.Paragraphs
        | Nested(nestedSlides) ->
            nestedSlides
            |> List.collect (fun slideData -> wrappedInSection slideData.Properties slideData.Paragraphs)
            |> wrappedInSection Map.empty

    let extractSlide paragraphs =
        let extractSlideData paragraphs : SlideData =
            let properties, data =
                match paragraphs with
                | ListBlock(_, items, _) :: data ->
                    try
                        getProperties items, data
                    with _ -> [], paragraphs
                | _ -> [], paragraphs

            { Properties = properties |> Map.ofList
              Paragraphs = data }

        // sub-section is separated by ---
        let nestedSlides =
            paragraphs
            |> splitBy (isRule '-')
            |> List.map extractSlideData

        match nestedSlides with
        | [ slideData ] -> Simple slideData
        | _ -> Nested nestedSlides

    let slides = List.map extractSlide slideData
    let paragraphs = List.collect getParagraphsFromSlide slides
    { Properties = properties
      Slides = slides
      Document = MarkdownDocument(paragraphs, doc.DefinedLinks) }
