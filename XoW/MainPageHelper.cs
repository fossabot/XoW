﻿using Microsoft.Toolkit.Uwp;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using XoW.Models;
using XoW.Services;

namespace XoW
{
    partial class MainPage : Page
    {
        private async Task<string> GetCdnUrl() => (await AnonBbsApiClient.GetCdnAsync()).First().Url;

        private async Task RefreshForumsAsync()
        {
            GlobalState.ForumAndIdLookup.Clear();

            var forumGroups = await AnonBbsApiClient.GetForumGroupsAsync();

            forumGroups
                .SelectMany(fg => fg.Forums)
                .ToList()
                .ForEach(f => GlobalState.ForumAndIdLookup.Add(f.Name, f.Id));

            // 版面组和版面按照Sort排序，保证以正确的顺序展示
            forumGroups.OrderBy(fg =>
            {
                fg.Forums = fg.Forums.OrderBy(f => f.Sort).ToList();
                return fg;
            });

            _navigationItems.Clear();

            forumGroups.ForEach(fg =>
            {
                // 版面组名作为导航栏Header
                var navigationHeader = new NavigationViewItemHeader
                {
                    Content = fg.Name,
                    Name = fg.Name,
                    // 在导航栏折叠时，隐藏导航栏Header
                    Visibility = ForumListNavigation.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed,
                };
                _navigationItems.Add(navigationHeader);

                // 遍历版面组下的版面，依次插入导航栏
                fg.Forums.ToList().ForEach(f =>
                {
                    var navigationItem = new NavigationViewItem
                    {
                        Content = f.Name,
                        Name = f.Name,
                        // 取版面名第一个字作为图标，在导航栏折叠时展示
                        Icon = new FontIcon
                        {
                            // 是个Windows肯定会带微软雅黑的吧
                            FontFamily = new FontFamily("Microsoft YaHei"),
                            Glyph = f.Name.First().ToString()
                        },
                        DataContext = f.Id.ToString(),
                    };
                    _navigationItems.Add(navigationItem);
                });

                _navigationItems.Add(new NavigationViewItemSeparator());
            });

            // 版面导航栏加载完成后，默认选择第一项，即默认展示时间线
            ForumListNavigation.SelectedItem = _navigationItems.Where(item => item is NavigationViewItem).First();
        }

        private void RefreshThreads()
        {
            MainPageProgressBar.Visibility = Visibility.Visible;

            if (GlobalState.CurrentForumId == Constants.TimelineForumId)
            {
                ButtonCreateThread.IsEnabled = false;
                ThreadsListView.ItemsSource = new IncrementalLoadingCollection<TimelineForumThreadSource, Grid>();
            }
            else
            {
                ButtonCreateThread.IsEnabled = true;
                ThreadsListView.ItemsSource = new IncrementalLoadingCollection<NormalForumThreadSource, Grid>();
            }

            MainPageProgressBar.Visibility = Visibility.Collapsed;
        }

        private void RefreshReplies()
        {
            Replies.ItemsSource = new IncrementalLoadingCollection<ThreadReplySource, Grid>();
        }
    }
}
