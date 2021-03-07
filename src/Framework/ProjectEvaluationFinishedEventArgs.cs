// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework.Profiler;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Framework
{
    /// <summary>
    /// Arguments for the project evaluation finished event.
    /// </summary>
    [Serializable]
    public sealed class ProjectEvaluationFinishedEventArgs : BuildStatusEventArgs
    {
        /// <summary>
        /// Initializes a new instance of the ProjectEvaluationFinishedEventArgs class.
        /// </summary>
        public ProjectEvaluationFinishedEventArgs()
        {
        }

        /// <summary>
        /// Initializes a new instance of the ProjectEvaluationFinishedEventArgs class.
        /// </summary>
        public ProjectEvaluationFinishedEventArgs(string message, params object[] messageArgs)
            : base(message, null, null, DateTime.UtcNow, messageArgs)
        {
        }

        /// <summary>
        /// Gets or sets the full path of the project that started evaluation.
        /// </summary>
        public string ProjectFile { get; set; }

        /// <summary>
        /// The result of profiling a project.
        /// </summary>
        /// <remarks>
        /// Null if profiling is not turned on
        /// </remarks>
        public ProfilerResult? ProfilerResult { get; set; }

        public IEnumerable Properties { get; set; }

        public IEnumerable Items { get; set; }

        public IEnumerable GlobalProperties { get; set; }

        internal override void CreateFromStream(BinaryReader reader, int version)
        {
            timestamp = reader.ReadTimestamp();
            BuildEventContext = reader.ReadOptionalBuildEventContext();
            ProjectFile = reader.ReadOptionalString();
            Properties = reader.ReadProperties();
            Items = ReadItems(reader);
            ProfilerResult = ReadProfilerResult();
        }

        private IList ReadItems(BinaryReader reader)
        {
            var list = new ArrayList();

            int count = reader.Read7BitEncodedInt();
            for (int i = 0; i < count; i++)
            {
                var item = ReadItem(reader);
                list.Add(item);
            }

            return list;
        }

        private object ReadItem(BinaryReader reader)
        {
            string itemSpec = reader.ReadString();
            int metadataCount = reader.Read7BitEncodedInt();
            if (metadataCount == 0)
            {
                return new TaskItemData(itemSpec, metadata: null);
            }

            var metadata = DictionaryFactory(metadataCount);
            for (int i = 0; i < metadataCount; i++)
            {
                string key = reader.ReadString();
                string value = reader.ReadString();
                if (key != null)
                {
                    metadata.Add(key, value);
                }
            }

            var taskItem = new TaskItemData(itemSpec, metadata);
            return taskItem;
        }

        internal override void WriteToStream(BinaryWriter writer)
        {
            writer.WriteTimestamp(timestamp);
            writer.WriteOptionalBuildEventContext(BuildEventContext);
            writer.WriteOptionalString(ProjectFile);
            WriteProperties();
            WriteItems(writer, Items);
            WriteProfilerResult();
        }

        private void WriteProperties(BinaryWriter writer)
        {
            if (Properties == null)
            {
                writer.Write((byte)0);
                return;
            }


        }

        private void WriteItems(BinaryWriter writer, IList items)
        {
            if (items == null)
            {
                writer.Write7BitEncodedInt(0);
                return;
            }

            int count = items.Count;
            writer.Write7BitEncodedInt(count);

            for (int i = 0; i < count; i++)
            {
                var item = items[i];
                WriteItem(writer, item);
            }
        }

        private void WriteItem(BinaryWriter writer, object item)
        {
            if (item is ITaskItem taskItem)
            {
                writer.Write(taskItem.ItemSpec);
                if (LogItemMetadata)
                {
                    WriteMetadata(writer, taskItem);
                }
                else
                {
                    writer.Write7BitEncodedInt(0);
                }
            }
            else // string or ValueType
            {
                writer.Write(item?.ToString() ?? "");
                writer.Write7BitEncodedInt(0);
            }
        }

        [ThreadStatic]
        private static List<KeyValuePair<string, string>> reusableMetadataList;

        private void WriteMetadata(BinaryWriter writer, ITaskItem taskItem)
        {
            if (reusableMetadataList == null)
            {
                reusableMetadataList = new List<KeyValuePair<string, string>>();
            }

            // WARNING: Can't use AddRange here because CopyOnWriteDictionary in Microsoft.Build.Utilities.v4.0.dll
            // is broken. Microsoft.Build.Utilities.v4.0.dll loads from the GAC by XAML markup tooling and it's
            // implementation doesn't work with AddRange because AddRange special-cases ICollection<T> and
            // CopyOnWriteDictionary doesn't implement it properly.
            foreach (var kvp in taskItem.EnumerateMetadata())
            {
                reusableMetadataList.Add(kvp);
            }

            writer.Write7BitEncodedInt(reusableMetadataList.Count);
            if (reusableMetadataList.Count == 0)
            {
                return;
            }

            foreach (var kvp in reusableMetadataList)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value);
            }

            reusableMetadataList.Clear();
        }
    }
}
