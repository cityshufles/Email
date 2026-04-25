using System;
using System.Collections.Generic;
using Email.Services.State;
using Email.TourTreeViewShapedData.Models;
using Microsoft.AspNetCore.Components;

namespace Email.Components.Tours
{
    public partial class TourTreeDisplay : ComponentBase
    {
        private static readonly TimeSpan DashboardCacheTtl = TimeSpan.FromMinutes(5);

        [Inject] protected TourDashboardCacheState DashboardCacheState { get; set; } = default!;

        private void RestoreTreeCacheStateIfAvailable()
        {
            if (TourData?.TreeNodes == null || TourData.TreeNodes.Count == 0)
            {
                return;
            }

            if (!DashboardCacheState.TryGetFreshSnapshot(DashboardCacheTtl, out var snapshot) || snapshot?.TreeState == null)
            {
                return;
            }

            var treeState = snapshot.TreeState;

            if (treeState.SelectedGuideIdByTour != null && treeState.SelectedGuideIdByTour.Count > 0)
            {
                foreach (var pair in treeState.SelectedGuideIdByTour)
                {
                    selectedGuideIdByTour[pair.Key] = pair.Value;
                }
            }

            if (treeState.SelectedMessageIdByTour != null && treeState.SelectedMessageIdByTour.Count > 0)
            {
                foreach (var pair in treeState.SelectedMessageIdByTour)
                {
                    selectedMessageIdByTour[pair.Key] = pair.Value;
                }
            }

            if (treeState.ExpandedNodePaths != null && treeState.ExpandedNodePaths.Count > 0)
            {
                ApplyExpandedNodeState(TourData.TreeNodes, treeState.ExpandedNodePaths, string.Empty);
            }
        }

        private void PersistTreeCacheState()
        {
            DashboardCacheState.SaveTreeState(BuildCurrentTreeState());
        }

        private TourTreeDisplayCacheState BuildCurrentTreeState()
        {
            return new TourTreeDisplayCacheState
            {
                ExpandedNodePaths = CaptureExpandedNodePaths(TourData?.TreeNodes),
                SelectedGuideIdByTour = new Dictionary<string, int?>(selectedGuideIdByTour, StringComparer.OrdinalIgnoreCase),
                SelectedMessageIdByTour = new Dictionary<string, string?>(selectedMessageIdByTour, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static HashSet<string> CaptureExpandedNodePaths(IEnumerable<ShapedTreeNode>? nodes)
        {
            var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (nodes == null)
            {
                return expanded;
            }

            CaptureExpandedNodePathsRecursive(nodes, string.Empty, expanded);
            return expanded;
        }

        private static void CaptureExpandedNodePathsRecursive(IEnumerable<ShapedTreeNode> nodes, string parentPath, HashSet<string> expanded)
        {
            foreach (var node in nodes)
            {
                var path = BuildNodePath(parentPath, node);
                if (node.IsExpanded)
                {
                    expanded.Add(path);
                }

                if (node.Children != null && node.Children.Count > 0)
                {
                    CaptureExpandedNodePathsRecursive(node.Children, path, expanded);
                }
            }
        }

        private static void ApplyExpandedNodeState(IEnumerable<ShapedTreeNode> nodes, HashSet<string> expandedNodePaths, string parentPath)
        {
            foreach (var node in nodes)
            {
                var path = BuildNodePath(parentPath, node);
                node.IsExpanded = expandedNodePaths.Contains(path);

                if (node.Children != null && node.Children.Count > 0)
                {
                    ApplyExpandedNodeState(node.Children, expandedNodePaths, path);
                }
            }
        }

        private static string BuildNodePath(string parentPath, ShapedTreeNode node)
        {
            var segment = $"{node.NodeType ?? string.Empty}:{node.Label ?? string.Empty}";
            return string.IsNullOrWhiteSpace(parentPath) ? segment : $"{parentPath}>{segment}";
        }
    }
}
