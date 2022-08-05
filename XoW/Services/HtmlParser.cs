﻿using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using HtmlAgilityPack;
using Microsoft.Toolkit.Uwp.Helpers;
using XoW.Utils;

namespace XoW.Services
{
    public static class HtmlParser
    {
        private const string AttributeHyperlink = "href";
        private const string AttributeStyle = "style";
        private const string AttributeStyleParamColor = "color";
        private const string AttributeStyleParamFontWeight = "font-weight";
        private const string XPathBrNodeAnywhere = "//br";
        private const string XPathTextNodeAnywhere = "//text()";
        private const string XPathAttributeStyle = $"@{AttributeStyle}";
        private const string XPathAttributeFontWeight = "";

        public static List<TextBlock> ParseHtmlIntoTextBlocks(string htmlString)
        {
            var rootHtmlDoc = new HtmlDocument();
            rootHtmlDoc.LoadHtml(htmlString);
            var firstTextNode = rootHtmlDoc.DocumentNode.SelectNodes(XPathTextNodeAnywhere).FirstOrDefault();

            bool shouldBoldForAllTextBlocks = false;
            Color? textBlockGlobalColor = null;
            GetGlobalStyleForAllTextBlocks(firstTextNode, ref shouldBoldForAllTextBlocks, ref textBlockGlobalColor);

            var textBlocksForContents = new List<TextBlock>();
            var linesOfHtmlString = htmlString.Split(Environment.NewLine);
            foreach (var line in linesOfHtmlString)
            {
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(line);

                if (htmlDoc.DocumentNode.Descendants("br").Any())
                {
                    foreach (var node in htmlDoc.DocumentNode.SelectNodes(XPathBrNodeAnywhere))
                    {
                        node.Remove();
                    }
                }

                var content = htmlDoc.DocumentNode.InnerText;
                var textBlock = ComponentsBuilder.CreateTextBlock(HtmlEntity.DeEntitize(content.Trim()));

                if (shouldBoldForAllTextBlocks)
                {
                    textBlock.FontWeight = FontWeights.Bold;
                }

                if (textBlockGlobalColor != null)
                {
                    textBlock.Foreground = new SolidColorBrush((Color)textBlockGlobalColor);
                }

                textBlocksForContents.Add(textBlock);
            }

            return textBlocksForContents;
        }

        private static void GetGlobalStyleForAllTextBlocks(HtmlNode parentNodeOfTheFirstTextNode, ref bool shouldBold, ref Color? textBlockGlobalColor)
        {
            // 先直接递归到顶层节点
            // 然后逐层在Span节点中寻找style属性
            // 接下来寻找color和font-weight
            if (parentNodeOfTheFirstTextNode.ParentNode != null)
            {
                GetGlobalStyleForAllTextBlocks(parentNodeOfTheFirstTextNode.ParentNode, ref shouldBold, ref textBlockGlobalColor);
            }

            var styleAttributeValue = parentNodeOfTheFirstTextNode.GetAttributeValue(AttributeStyle, null);
            if (styleAttributeValue == null)
            {
                return;
            }

            var attributeValues = styleAttributeValue.Split(";").Select(str => str.Trim()).ToList();

            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            foreach (var styleParam in attributeValues)
            {
                var paramPair = styleParam.Split(":").Select(str => str.Trim()).ToList();
                switch (paramPair[0])
                {
                    case "color":
                        textBlockGlobalColor = paramPair[1].FirstCharToUpper().ToColor();
                        break;
                    case "font-weight":
                        shouldBold = paramPair[1].Equals("bold", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }
        }
    }
}
