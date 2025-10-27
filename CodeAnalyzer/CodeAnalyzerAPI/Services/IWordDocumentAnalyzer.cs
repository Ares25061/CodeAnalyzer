using CodeAnalyzerLibrary;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeAnalyzerAPI.Services
{
    public interface IWordDocumentAnalyzer
    {
        Task<WordDocumentAnalysisResult> AnalyzeDocumentFormattingAsync(string filePath, DocumentFormattingRules rules);
        Task<string> ExtractDocumentTextAsync(string filePath);
    }

    public class WordDocumentAnalyzer : IWordDocumentAnalyzer
    {
        private readonly ILogger<WordDocumentAnalyzer> _logger;

        public WordDocumentAnalyzer(ILogger<WordDocumentAnalyzer> logger)
        {
            _logger = logger;
        }

        public async Task<WordDocumentAnalysisResult> AnalyzeDocumentFormattingAsync(string filePath, DocumentFormattingRules rules)
        {
            var result = new WordDocumentAnalysisResult { FilePath = filePath };

            try
            {
                using (var document = WordprocessingDocument.Open(filePath, false))
                {
                    var body = document.MainDocumentPart?.Document.Body;
                    if (body == null)
                    {
                        result.Error = "Не удалось прочитать содержимое документа";
                        return result;
                    }

                    AnalyzeDocumentStyles(document, result, rules);

                    AnalyzeParagraphs(body, result, rules);

                    CalculateStatistics(body, result);

                    _logger.LogInformation("Анализ Word документа завершен: {FileName}, параграфов: {ParagraphsCount}",
                        Path.GetFileName(filePath), result.ParagraphsCount);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при анализе Word документа: {FilePath}", filePath);
                result.Error = ex.Message;
                return result;
            }
        }

        private void AnalyzeDocumentStyles(WordprocessingDocument document, WordDocumentAnalysisResult result, DocumentFormattingRules rules)
        {
            var stylesPart = document.MainDocumentPart?.StyleDefinitionsPart;
            if (stylesPart?.Styles == null) return;

            var normalStyle = stylesPart.Styles.Elements<Style>()
                .FirstOrDefault(style => style.Type == StyleValues.Paragraph && style.StyleId == "Normal");

            if (normalStyle != null)
            {
                AnalyzeStyle(normalStyle, result, rules, "Основной стиль");
            }
        }

        private void AnalyzeParagraphs(Body body, WordDocumentAnalysisResult result, DocumentFormattingRules rules)
        {
            var paragraphs = body.Elements<Paragraph>().ToList();
            result.ParagraphsCount = paragraphs.Count;

            int analyzedParagraphs = 0;
            int paragraphsWithCorrectFont = 0;
            int paragraphsWithCorrectSize = 0;
            int paragraphsWithCorrectSpacing = 0;
            int paragraphsWithIndent = 0;

            foreach (var paragraph in paragraphs.Take(100))
            {
                var paragraphProperties = paragraph.ParagraphProperties;
                var runProperties = paragraph.Elements<Run>().FirstOrDefault()?.RunProperties;

                if (runProperties?.RunFonts != null)
                {
                    var font = runProperties.RunFonts.Ascii?.Value;
                    if (!string.IsNullOrEmpty(font))
                    {
                        result.ActualFont = font;
                        if (rules.AllowedFonts.Contains(font, StringComparer.OrdinalIgnoreCase))
                        {
                            paragraphsWithCorrectFont++;
                        }
                    }
                }

                if (runProperties?.FontSize != null)
                {
                    var fontSizeVal = runProperties.FontSize.Val;
                    if (fontSizeVal != null && double.TryParse(fontSizeVal.Value.ToString(), out double fontSize))
                    {
                        fontSize = fontSize / 2.0;
                        result.ActualFontSize = fontSize;
                        if (Math.Abs(fontSize - rules.ExpectedFontSize) <= 0.5)
                        {
                            paragraphsWithCorrectSize++;
                        }
                    }
                }

                if (paragraphProperties?.SpacingBetweenLines != null)
                {
                    var lineSpacingVal = paragraphProperties.SpacingBetweenLines.Line;
                    if (lineSpacingVal != null && double.TryParse(lineSpacingVal.Value.ToString(), out double lineSpacing))
                    {
                        lineSpacing = lineSpacing / 240.0;
                        result.ActualLineSpacing = lineSpacing;
                        if (Math.Abs(lineSpacing - rules.ExpectedLineSpacing) <= 0.1)
                        {
                            paragraphsWithCorrectSpacing++;
                        }
                    }
                }

                if (paragraphProperties?.Indentation != null)
                {
                    var firstLineIndent = paragraphProperties.Indentation.FirstLine;
                    if (firstLineIndent != null && long.TryParse(firstLineIndent.Value.ToString(), out long indentValue))
                    {
                        if (indentValue > 0)
                        {
                            paragraphsWithIndent++;
                        }
                    }
                }

                analyzedParagraphs++;
            }

            if (result.ActualFontSize == 0)
                result.ActualFontSize = 14.0;
            if (result.ActualLineSpacing == 0)
                result.ActualLineSpacing = 1.0;

            AddFormattingChecks(result, rules, analyzedParagraphs, paragraphsWithCorrectFont,
                paragraphsWithCorrectSize, paragraphsWithCorrectSpacing, paragraphsWithIndent);
        }

        private void AddFormattingChecks(WordDocumentAnalysisResult result, DocumentFormattingRules rules,
            int totalParagraphs, int correctFont, int correctSize, int correctSpacing, int withIndent)
        {
            result.FormattingChecks.Add(new FormattingCheckResult
            {
                CheckName = "Основной шрифт",
                Passed = correctFont > totalParagraphs * 0.8, 
                Message = (correctFont > totalParagraphs * 0.8) ?
                    "Шрифт соответствует требованиям" :
                    "Обнаружены несоответствия шрифта",
                ExpectedValue = rules.ExpectedFont,
                ActualValue = string.IsNullOrEmpty(result.ActualFont) ? "Не определен" : result.ActualFont
            });

            result.FormattingChecks.Add(new FormattingCheckResult
            {
                CheckName = "Размер шрифта",
                Passed = correctSize > totalParagraphs * 0.8,
                Message = (correctSize > totalParagraphs * 0.8) ?
                    "Размер шрифта соответствует требованиям" :
                    "Обнаружены несоответствия размера шрифта",
                ExpectedValue = rules.ExpectedFontSize.ToString(),
                ActualValue = result.ActualFontSize.ToString("F1")
            });

            result.FormattingChecks.Add(new FormattingCheckResult
            {
                CheckName = "Межстрочный интервал",
                Passed = correctSpacing > totalParagraphs * 0.8,
                Message = (correctSpacing > totalParagraphs * 0.8) ?
                    "Интервал соответствует требованиям" :
                    "Обнаружены несоответствия интервала",
                ExpectedValue = rules.ExpectedLineSpacing.ToString("F1"),
                ActualValue = result.ActualLineSpacing.ToString("F1")
            });

            if (rules.RequireParagraphIndent)
            {
                result.HasParagraphIndents = withIndent > totalParagraphs * 0.8;
                result.FormattingChecks.Add(new FormattingCheckResult
                {
                    CheckName = "Отступы абзацев",
                    Passed = result.HasParagraphIndents,
                    Message = result.HasParagraphIndents ?
                        "Отступы абзацев присутствуют" :
                        "Отсутствуют отступы в начале абзацев",
                    ExpectedValue = "Требуется",
                    ActualValue = result.HasParagraphIndents ? "Присутствуют" : "Отсутствуют"
                });
            }

            CheckDocumentStructure(result, totalParagraphs);
        }

        private void CheckDocumentStructure(WordDocumentAnalysisResult result, int totalParagraphs)
        {
            if (totalParagraphs < 10)
            {
                result.StructureIssues.Add("Малое количество параграфов для академической работы");
            }

            if (result.SectionsCount < 3)
            {
                result.StructureIssues.Add("Возможно недостаточно разделов в документе");
            }
        }

        private void CalculateStatistics(Body body, WordDocumentAnalysisResult result)
        {
            var sections = body.Elements<Paragraph>()
                .Where(p =>
                    (p.ParagraphProperties?.PageBreakBefore != null) ||
                    IsHeadingParagraph(p))
                .Count();

            result.SectionsCount = Math.Max(1, sections);
            var totalText = ExtractTextFromBody(body);
            result.PagesCount = Math.Max(1, totalText.Length / 2000); 
        }

        private bool IsHeadingParagraph(Paragraph paragraph)
        {
            var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val;
            if (style == null) return false;

            var styleValue = style.Value;
            return !string.IsNullOrEmpty(styleValue) &&
                   (styleValue.Contains("Heading", StringComparison.OrdinalIgnoreCase) ||
                    styleValue.Contains("Head", StringComparison.OrdinalIgnoreCase));
        }

        private void AnalyzeStyle(Style style, WordDocumentAnalysisResult result, DocumentFormattingRules rules, string styleName)
        {
            var runProperties = style.StyleRunProperties;

            if (runProperties?.RunFonts != null)
            {
                var font = runProperties.RunFonts.Ascii?.Value;
                if (!string.IsNullOrEmpty(font) && string.IsNullOrEmpty(result.ActualFont))
                {
                    result.ActualFont = font;
                }
            }

            if (runProperties?.FontSize != null)
            {
                var fontSizeVal = runProperties.FontSize.Val;
                if (fontSizeVal != null && double.TryParse(fontSizeVal.Value.ToString(), out double fontSize))
                {
                    fontSize = fontSize / 2.0;
                    if (fontSize > 0 && result.ActualFontSize == 0)
                    {
                        result.ActualFontSize = fontSize;
                    }
                }
            }
        }

        public async Task<string> ExtractDocumentTextAsync(string filePath)
        {
            try
            {
                using (var document = WordprocessingDocument.Open(filePath, false))
                {
                    var body = document.MainDocumentPart?.Document.Body;
                    if (body == null) return string.Empty;

                    var text = ExtractTextFromBody(body);
                    return await Task.FromResult(text);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при извлечении текста из Word документа: {FilePath}", filePath);
                return string.Empty;
            }
        }

        private string ExtractTextFromBody(Body body)
        {
            var textBuilder = new StringBuilder();

            foreach (var paragraph in body.Elements<Paragraph>())
            {
                foreach (var run in paragraph.Elements<Run>())
                {
                    foreach (var textElement in run.Elements<DocumentFormat.OpenXml.Wordprocessing.Text>())
                    {
                        textBuilder.Append(textElement.Text);
                    }
                }
                textBuilder.AppendLine(); 
            }

            return textBuilder.ToString();
        }
    }
}