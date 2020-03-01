﻿using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.CodeConverter.Shared;
using ICSharpCode.CodeConverter.Util;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using LanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion;
using System;

namespace ICSharpCode.CodeConverter.CSharp
{
    /// <remarks>
    /// Can be stateful, need a new one for each project
    /// </remarks>
    internal class VBToCSProjectContentsConverter : IProjectContentsConverter
    {
        private readonly ConversionOptions _conversionOptions;
        private CSharpCompilation _csharpViewOfVbSymbols;
        private Project _convertedCsProject;

        /// <summary>
        /// It's really hard to change simplifier options since everything is done on the Object hashcode of internal fields.
        /// I wanted to avoid saying "default" instead of "default(string)" because I don't want to force a later language version on people in such a common case.
        /// This will have that effect, but also has the possibility of failing to interpret code output by this converter.
        /// If this has such unintended effects in future, investigate the code that loads options from an editorconfig file
        /// </summary>
        private static readonly CSharpParseOptions DoNotAllowImplicitDefault = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp7);

        private Project _csharpReferenceProject;
        private readonly IProgress<ConversionProgress> _progress;
        private readonly CancellationToken _cancellationToken;

        public VBToCSProjectContentsConverter(ConversionOptions conversionOptions, IProgress<ConversionProgress> progress, CancellationToken cancellationToken)
        {
            _conversionOptions = conversionOptions;
            _progress = progress;
            _cancellationToken = cancellationToken;
        }

        public string RootNamespace => _conversionOptions.RootNamespaceOverride ??
                                       ((VisualBasicCompilationOptions)Project.CompilationOptions).RootNamespace;

        public async Task InitializeSourceAsync(Project project)
        {
            var cSharpCompilationOptions = CSharpCompiler.CreateCompilationOptions();
            _convertedCsProject = project.ToProjectFromAnyOptions(cSharpCompilationOptions, DoNotAllowImplicitDefault);
            _csharpReferenceProject = project.CreateReferenceOnlyProjectFromAnyOptions(cSharpCompilationOptions);
            _csharpViewOfVbSymbols = (CSharpCompilation) await _csharpReferenceProject.GetCompilationAsync(_cancellationToken);
            Project = await project.WithRenamedMergedMyNamespace(_cancellationToken);
        }

        string IProjectContentsConverter.LanguageVersion { get { return LanguageVersion.Default.ToDisplayString(); } }

        public Project Project { get; private set; }

        public async Task<SyntaxNode> SingleFirstPass(Document document)
        {
            return await VisualBasicConverter.ConvertCompilationTree(document, _csharpViewOfVbSymbols, _csharpReferenceProject, _cancellationToken);
        }

        public async Task<(Project project, List<WipFileConversion<DocumentId>> firstPassDocIds)>
            GetConvertedProject(WipFileConversion<SyntaxNode>[] firstPassResults)
        {
            var (project, docIds) = _convertedCsProject.WithDocuments(firstPassResults);
            return (await project.RenameMergedNamespaces(_cancellationToken), docIds);
        }
    }
}