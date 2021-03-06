﻿using CSDeskBand;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EverythingToolbar
{
	public partial class ToolbarControl : UserControl
	{
		private CancellationTokenSource cancellationTokenSource;
		private CancellationToken cancellationToken;
		private static Edge taskbarEdge;
		private readonly int searchBlockSize = 100;

		public ToolbarControl()
		{
			InitializeComponent();
			LoadThemes();
			ApplyTheme(Properties.Settings.Default.theme);

			// Fixes #3
			if (Properties.Settings.Default.sortBy < 1)
			{
				Properties.Settings.Default.sortBy = 1;
				Properties.Settings.Default.Save();
			}

			(SortByMenu.Items[Properties.Settings.Default.sortBy - 1] as MenuItem).IsChecked = true;

			searchResultsPopup.searchResultsView.EndOfListReached += OnEndOfListReached;
			searchResultsPopup.searchResultsView.FilterChanged += OnFilterChanged;
			searchResultsPopup.Closed += SearchResultsPopup_Closed;
		}

		public static void SetTaskbarEdge(Edge edge)
		{
			taskbarEdge = edge;
		}

		private void OnFilterChanged(object sender, FilterChangedEventArgs e)
		{
			EverythingSearch.Instance.SearchMacro = e.Filter;
			StartSearch(EverythingSearch.Instance.SearchTerm);
		}

		private void OnEndOfListReached(object sender, EndOfListReachedEventArgs e)
		{
			RequestSearchResults(e.ItemCount, searchBlockSize);
			searchResultsPopup.searchResultsView.ScrollToVerticalOffset(e.VerticalOffset);
		}

		public void StartSearch(string searchTerm)
		{
			searchResultsPopup.searchResultsView.Clear();
			EverythingSearch.Instance.SearchTerm = searchTerm;
			RequestSearchResults(0, searchBlockSize);
		}

		private void RequestSearchResults(int offset, int count)
		{
			cancellationTokenSource?.Cancel();
			cancellationTokenSource = new CancellationTokenSource();
			cancellationToken = cancellationTokenSource.Token;

			Task.Run(() =>
			{
				foreach(SearchResult searchResult in EverythingSearch.Instance.Query(offset, count))
				{
					searchResultsPopup.searchResultsView.AddSearchResult(searchResult);
				}
			}, cancellationToken);
		}

		private void SearchResultsPopup_Closed(object sender, EventArgs e)
		{
			if (!searchBox.IsKeyboardFocused)
				keyboardFocusCapture.Focus();

			searchBox.Clear();
		}

		private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
		{
			if (searchResultsPopup.IsMouseOver && !searchResultsPopup.searchResultsView.SearchResultsListView.IsMouseOver)
			{
				searchBox.Focus();
			}
			else
			{
				searchResultsPopup.StaysOpen = false;
			}
		}

		private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (searchBox.Text.Length == 0)
			{
				searchResultsPopup.Close();
				searchResultsPopup.searchResultsView.AllTab.IsSelected = true;
				return;
			}

			searchResultsPopup.Open(taskbarEdge);
			StartSearch(searchBox.Text);
		}

		private void CSDeskBandWpf_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (!searchResultsPopup.IsOpen)
				return;

			if (e.Key == Key.Up)
			{
				searchResultsPopup.searchResultsView.SelectPreviousSearchResult();
			}
			else if (e.Key == Key.Down)
			{
				searchResultsPopup.searchResultsView.SelectNextSearchResult();
			}
			else if (e.Key == Key.Enter)
			{
				if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
				{
					string path = "";
					if (searchResultsPopup.searchResultsView.SearchResultsListView.SelectedIndex >= 0)
						path = (searchResultsPopup.searchResultsView.SearchResultsListView.SelectedItem as SearchResult).FullPathAndFileName;
					EverythingSearch.Instance.OpenLastSearchInEverything(path);
					return;
				}
				searchResultsPopup.searchResultsView.OpenSelectedSearchResult();
			}
			else if (e.Key == Key.Escape)
			{
				searchResultsPopup.Close();
				keyboardFocusCapture.Focus();
				Keyboard.ClearFocus();
			}
		}

		private void OpenAboutWindow(object sender, RoutedEventArgs e)
		{
			Window about = new About();
			about.Show();
		}

		private void MenuItem_Click(object sender, RoutedEventArgs e)
		{
			Properties.Settings.Default.Save();
			StartSearch(EverythingSearch.Instance.SearchTerm);
		}

		private void MenuItem_SortBy_Click(object sender, RoutedEventArgs e)
		{
			MenuItem itemChecked = (MenuItem)sender;
			MenuItem itemParent = (MenuItem)itemChecked.Parent;

			for (int i = 0; i < itemParent.Items.Count; i++)
			{
				if (itemParent.Items[i] == itemChecked)
				{
					(itemParent.Items[i] as MenuItem).IsChecked = true;
					Properties.Settings.Default.sortBy = i + 1;
					continue;
				}

				(itemParent.Items[i] as MenuItem).IsChecked = false;
			}

			Properties.Settings.Default.Save();
			StartSearch(EverythingSearch.Instance.SearchTerm);
		}

		public void LoadThemes()
		{
			string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			string themesFolder = Path.Combine(assemblyFolder, "Themes");

			foreach (var themePath in Directory.EnumerateFiles(themesFolder, "*.xaml"))
			{
				string themeName = Path.GetFileNameWithoutExtension(themePath);
				MenuItem mi = new MenuItem() { IsCheckable = true, Header = themeName };
				if (themeName == Properties.Settings.Default.theme)
				{
					mi.IsChecked = true;
				}
				mi.Click += MenuItem_Theme_Click;
				ThemeMenu.Items.Add(mi);
			}
		}

		private void MenuItem_Theme_Click(object sender, RoutedEventArgs e)
		{
			MenuItem itemChecked = (MenuItem)sender;
			MenuItem itemParent = (MenuItem)itemChecked.Parent;

			for (int i = 0; i < itemParent.Items.Count; i++)
			{
				if (itemParent.Items[i] == itemChecked)
				{
					(itemParent.Items[i] as MenuItem).IsChecked = true;
					Properties.Settings.Default.theme = itemChecked.Header.ToString();
					continue;
				}

				(itemParent.Items[i] as MenuItem).IsChecked = false;
			}

			if (ApplyTheme(itemChecked.Header.ToString()))
				Properties.Settings.Default.Save();
		}

		bool ApplyTheme(string themeName)
		{
			string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			string themePath = Path.Combine(assemblyFolder, "Themes", themeName + ".xaml");

			if (!File.Exists(themePath))
			{
				ToolbarLogger.GetLogger("EverythingToolbar").Error("Theme file not found. Defaulting to 'Medium' theme.");
				themePath = Path.Combine(assemblyFolder, "Themes", "Medium.xaml");
			}

			try
			{
				Resources.MergedDictionaries.Clear();
				Resources.MergedDictionaries.Add(new ResourceDictionary() { Source = new Uri(themePath) });
				return true;
			}
			catch (Exception e)
			{
				ToolbarLogger.GetLogger("EverythingToolbar").Error(e, "Applying theme failed.");
				return false;
			}
		}
	}
}
