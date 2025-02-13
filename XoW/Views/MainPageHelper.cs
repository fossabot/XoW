﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Toolkit.Uwp;
using Microsoft.Toolkit.Uwp.UI.Controls;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using XoW.Models;
using XoW.Services;
using XoW.Utils;

namespace XoW.Views
{
    partial class MainPage : Page
    {
        private void InitializeStaticResources()
        {
            // 载入已添加的饼干
            ApplicationConfigurationHelper.LoadAllCookies();

            // 默认以当前选择的饼干发串
            NewThreadCookieSelectionComboBox.SelectedItem = GlobalState.Cookies.SingleOrDefault(cookie => cookie.Name == GlobalState.ObservableObject.CurrentCookie);
            NewReplyCookieSelectionComboBox.SelectedItem = GlobalState.Cookies.SingleOrDefault(cookie => cookie.Name == GlobalState.ObservableObject.CurrentCookie);

            // 加载订阅ID
            GlobalState.ObservableObject.SubscriptionId = ApplicationConfigurationHelper.GetSubscriptionId();

            var currentCookieName = ApplicationConfigurationHelper.GetCurrentCookie();
            var currentCookieValue = GlobalState.Cookies.SingleOrDefault(cookie => cookie.Name == currentCookieName)?.Cookie;
            if (!string.IsNullOrEmpty(currentCookieValue))
            {
                HttpClientService.ApplyCookie(currentCookieValue);
            }

            CreateEmoticonButtons(NewThreadEmoticonWrapPanel, OnNewThreadEmoticonButtonClicked);
            CreateEmoticonButtons(NewReplyEmoticonWrapPanel, OnNewReplyEmoticonButtonClicked);
        }

        private static async Task<string> GetCdnUrl() => (await AnoBbsApiClient.GetCdnAsync()).First().Url;

        private async Task RefreshForumsAsync()
        {
            GlobalState.ForumAndIdLookup = new Dictionary<string, (string, string)>();

            var forumGroups = await AnoBbsApiClient.GetForumGroupsAsync();

            // 版面组和版面按照Sort排序，保证以正确的顺序展示
            forumGroups = forumGroups.OrderBy(fg => fg.Sort).ToList();

            forumGroups.ForEach(fg =>
            {
                fg.Forums = fg.Forums.OrderBy(f => f.Sort).ToList();
            });

            forumGroups.SelectMany(fg => fg.Forums).ToList().ForEach(f => GlobalState.ForumAndIdLookup.Add(f.Name, (f.Id, f.permissionLevel)));

            _navigationItems.Clear();

            var favouriteThreadsNavigationItem = new NavigationViewItem
            {
                Content = Constants.FavouriteThreadNavigationItemName,
                Name = Constants.FavouriteThreadNavigationItemName,
                Icon = new SymbolIcon(Symbol.OutlineStar)
            };
            _navigationItems.Add(favouriteThreadsNavigationItem);

            forumGroups.ForEach(fg =>
            {
                // 版面组名作为导航栏Header
                var navigationHeader = new NavigationViewItemHeader
                {
                    Content = fg.Name,
                    Name = fg.Name,
                    // 在导航栏折叠时，隐藏导航栏Header
                    Visibility = ForumListNavigation.IsPaneOpen
                        ? Visibility.Visible
                        : Visibility.Collapsed
                };
                _navigationItems.Add(navigationHeader);

                // 遍历版面组下的版面，依次插入导航栏
                fg.Forums.ToList()
                    .ForEach(f =>
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
                            DataContext = f.Id.ToString()
                        };
                        _navigationItems.Add(navigationItem);
                    });

                _navigationItems.Add(new NavigationViewItemSeparator());
            });

            // 版面导航栏加载完成后，默认选择第一项，即默认展示时间线
            ForumListNavigation.SelectedItem = _navigationItems.First(item => item is NavigationViewItem && !_nonForumNavigationItems.Contains(item.Name));
        }

        private async Task RefreshThreads()
        {
            MainPageProgressBar.Visibility = Visibility.Visible;

            if (GlobalState.CurrentForumId == Constants.TimelineForumId)
            {
                var incrementalLoadingCollection = new IncrementalLoadingCollection<TimelineForumThreadSource, Grid>();
                await incrementalLoadingCollection.RefreshAsync();
                ThreadsListView.ItemsSource = incrementalLoadingCollection;
            }
            else
            {
                var incrementalLoadingCollection = new IncrementalLoadingCollection<NormalForumThreadSource, Grid>();
                await incrementalLoadingCollection.RefreshAsync();
                ThreadsListView.ItemsSource = incrementalLoadingCollection;
            }

            GlobalState.ObservableObject.ForumName = GlobalState.ForumAndIdLookup.Single(lookup => lookup.Value.forumId == GlobalState.CurrentForumId).Key;

            MainPageProgressBar.Visibility = Visibility.Collapsed;
        }

        private async Task RefreshRepliesForXamlBinding()
        {
            await RefreshReplies();
        }

        private async Task RefreshReplies(int pageNumber = 1)
        {
            MainPageProgressBar.Visibility = Visibility.Visible;

            var threadReplySource = new ThreadReplySource(pageNumber);
            var incrementalLoadingCollection = new IncrementalLoadingCollection<ThreadReplySource, Grid>(threadReplySource);
            await incrementalLoadingCollection.RefreshAsync();
            RepliesListView.ItemsSource = incrementalLoadingCollection;

            MainPageProgressBar.Visibility = Visibility.Collapsed;
        }

        private async Task RefreshPoOnlyReplies()
        {
            MainPageProgressBar.Visibility = Visibility.Visible;

            var incrementalLoadingCollection = new IncrementalLoadingCollection<PoOnlyThreadReplySource, Grid>();
            await incrementalLoadingCollection.RefreshAsync();
            RepliesListView.ItemsSource = incrementalLoadingCollection;

            MainPageProgressBar.Visibility = Visibility.Collapsed;
        }

        private async Task RefreshSubscriptions()
        {
            MainPageProgressBar.Visibility = Visibility.Visible;

            var itemsSource = new IncrementalLoadingCollection<SubscriptionSource, Grid>();
            itemsSource.OnEndLoading = () =>
            {
                foreach (var item in itemsSource)
                {
                    var contentParentStackPanel = item.Children.Single(element => ((StackPanel)element).Name == ComponentsBuilder.TopLevelStackPanel) as StackPanel;
                    var headerGrid = contentParentStackPanel.Children.Single(element => ((Grid)element).Name == ComponentsBuilder.ThreadHeaderParentGrid) as Grid;
                    var stackPanelForDeleteButton = headerGrid.Children.Single(element => ((StackPanel)element).Name == ComponentsBuilder.StackPanelForDeleteButton) as StackPanel;
                    var buttonForDeleteSubscription = stackPanelForDeleteButton.Children.Where(element => element is Button).Single(button => ((Button)button).Name == ComponentsBuilder.ButtonDeleteSubscriptionName) as Button;

                    // 确保这个EventHandler只被注册一次
                    buttonForDeleteSubscription.Click -= OnDeleteSubscriptionButtonClicked;
                    buttonForDeleteSubscription.Click += OnDeleteSubscriptionButtonClicked;
                }
            };

            await itemsSource.RefreshAsync();
            ThreadsListView.ItemsSource = itemsSource;

            MainPageProgressBar.Visibility = Visibility.Collapsed;
        }

        private void ResetAndShowRepliesPanel()
        {
            ButtonPoOnly.IsChecked = false;
            ContentRepliesGrid.Visibility = Visibility.Visible;
        }

        private void ShowSettingsGrid()
        {
            ContentGrid.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Visible;
        }

        private void ShowContentGrid()
        {
            ContentGrid.Visibility = Visibility.Visible;
            SettingsPage.Visibility = Visibility.Collapsed;
        }

        private void ShowNewThreadPanel() => NewThreadPanelGrid.Visibility = Visibility.Visible;

        private async Task HideNewThreadPanel(bool isNewThreadSent = false)
        {
            if (!isNewThreadSent && NewThreadPanelGrid.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(TextBoxNewThreadContent.Text))
            {
                await new ConfirmationContentDialog(ComponentContent.Notification, ConfirmationMessage.KeepContentConfirmation, secondaryButtonEventHandler: (_, _) => ResetNewThreadPanel(), primaryButtonContent: ComponentContent.KeepingIt, secondaryButtonContent: ComponentContent.DiscardIt).ShowAsync();
            }

            NewThreadPanelGrid.Visibility = Visibility.Collapsed;
        }

        private void ShowNewReplyPanel() => NewReplyPanelGrid.Visibility = Visibility.Visible;

        private async Task HideNewReplyPanel(bool isNewReplySent = false)
        {
            if (!isNewReplySent && NewReplyPanelGrid.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(TextBoxNewReplyContent.Text))
            {
                await new ConfirmationContentDialog(ComponentContent.Notification, ConfirmationMessage.KeepContentConfirmation, secondaryButtonEventHandler: (_, _) => ResetNewReplyPanel(), primaryButtonContent: ComponentContent.KeepingIt, secondaryButtonContent: ComponentContent.DiscardIt).ShowAsync();
            }

            NewReplyPanelGrid.Visibility = Visibility.Collapsed;
        }

        private void HideLargeImageView()
        {
            LargeImageView.Visibility = Visibility.Collapsed;
            GlobalState.LargeImageViewObjectReference.ResetState();
        }

        private void ResetNewThreadPanel()
        {
            TextBoxNewThreadUserName.Text = "";
            TextBoxNewThreadEmail.Text = "";
            TextBoxNewThreadTitle.Text = "";
            TextBoxNewThreadContent.Text = "";
            NewThreadCookieSelectionComboBox.SelectedItem = GlobalState.Cookies.SingleOrDefault(cookie => cookie.Name == GlobalState.ObservableObject.CurrentCookie);
            ForumSelectionComboBox.SelectedIndex = 0;
            ButtonNewThreadAttachPicture.DataContext = null;
            CheckBoxNewThreadWaterMark.IsChecked = true;
            ImagePreviewStackPanel.Visibility = Visibility.Collapsed;
        }

        private void ResetNewReplyPanel()
        {
            TextBoxNewReplyUserName.Text = "";
            TextBoxNewReplyEmail.Text = "";
            TextBoxNewReplyTitle.Text = "";
            TextBoxNewReplyContent.Text = "";
            NewReplyCookieSelectionComboBox.SelectedItem = GlobalState.Cookies.SingleOrDefault(cookie => cookie.Name == GlobalState.ObservableObject.CurrentCookie);
            ButtonNewReplyAttachPicture.DataContext = null;
            CheckBoxNewReplyWaterMark.IsChecked = true;
            ReplyImagePreviewStackPanel.Visibility = Visibility.Collapsed;
        }

        private void DisableSendButtonAndShowProgressBar(Button sendButton)
        {
            sendButton.IsEnabled = false;
            MainPageProgressBar.Visibility = Visibility.Visible;
        }

        private void EnableSendButtonAndHideProgressBar(Button sendButton)
        {
            sendButton.IsEnabled = true;
            MainPageProgressBar.Visibility = Visibility.Collapsed;
        }

        private void CreateEmoticonButtons(WrapPanel parentWrapPanel, RoutedEventHandler clickEventHandler)
        {
            foreach (var emoticon in Constants.Emoticons)
            {
                var button = new Button
                {
                    Content = emoticon.Key,
                    DataContext = emoticon.Value
                };

                button.Click += clickEventHandler;

                parentWrapPanel.Children.Add(button);
            }
        }
    }
}
