# Paper Build Process

## Quick Reference

```
paper-name.md  →  paper-name.tex  →  paper-name.pdf
   (write)        (format+figures)      (compile)
```

## Step 1: Write Content in .md

Write your paper content in markdown. This is your source of truth for text.

## Step 2: Port to .tex

Create a `.tex` file with this skeleton:

```latex
\documentclass[conference]{IEEEtran}
\usepackage{pgfplots, tikz, booktabs, listings, algorithm, algorithmic, amsmath, amsthm, hyperref}
\pgfplotsset{compat=1.18}

\begin{document}
\title{Your Title}
\author{...}
\maketitle

\begin{abstract}
...
\end{abstract}

% Sections go here

\bibliographystyle{IEEEtran}
\bibliography{refs}
\end{document}
```

Formatting conversions:
- `**bold**` → `\textbf{bold}`
- `# Heading` → `\section{Heading}`
- `## Subheading` → `\subsection{Subheading}`
- Markdown tables → `\begin{tabular}` with `booktabs` (`\toprule`, `\midrule`, `\bottomrule`)
- Math is the same: `$O(k)$` → `$O(k)$`

## Step 3: Add Figures (pgfplots)

Figures are written directly in LaTeX — no external images. Basic pattern:

**Bar chart:**
```latex
\begin{figure}[t]
\centering
\begin{tikzpicture}
\begin{axis}[ybar, bar width=12pt, symbolic x coords={A,B,C}, xtick=data,
    ylabel={Time (ms)}, nodes near coords, width=\columnwidth, height=5cm]
\addplot coordinates {(A, 3.5) (B, 4.2) (C, 3.8)};
\end{axis}
\end{tikzpicture}
\caption{Your caption.}\label{fig:your-label}
\end{figure}
```

**Line chart:**
```latex
\begin{axis}[xlabel={X}, ylabel={Y}, width=\columnwidth, height=5cm, legend pos=north west]
\addplot[color=red, mark=square*] coordinates {(1, 10) (2, 20) (3, 30)};
\addplot[color=blue, mark=*] coordinates {(1, 5) (2, 6) (3, 7)};
\legend{Series A, Series B}
\end{axis}
```

To update figures with new data, just change the numbers in `\addplot coordinates {...}`.

## Step 4: Compile to PDF

Requires Docker with `texlive/texlive` (one-time: `docker pull texlive/texlive`).

Run **twice** (second pass resolves cross-references):

```bash
docker run --rm \
  -v /path/to/papers/dir:/workdir \
  -w /workdir \
  texlive/texlive \
  pdflatex -interaction=nonstopmode paper-name.tex

# Run the same command again
docker run --rm \
  -v /path/to/papers/dir:/workdir \
  -w /workdir \
  texlive/texlive \
  pdflatex -interaction=nonstopmode paper-name.tex
```

Output: `paper-name.pdf` in the same directory.

## Troubleshooting

| Problem | Fix |
|---------|-----|
| `Undefined reference` | Run pdflatex a second time |
| Overfull hbox | Rephrase text or add `\-` hyphenation hints |
| Page count too high | Use `\scriptsize` in tables, reduce figure `height` |
