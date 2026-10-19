// Explicit bridge entry point for a REAL Engine presenter callback, never
// generated from test data, image classes, reward states or solver guesses.
// In your Engine-driven session call Begin(profile) and Observe(realEvent)
// on each actual PresentationEvent, then invoke the Editor menu command.
// This file is in Editor/, excluded from Windows/iOS/Android player assembly.
using System;
using System.IO;
using Match3.Engine.Runtime;
using UnityEditor;
using UnityEngine;

namespace Match3.Engine.Demo.Editor
{
    public static class NeutralVisualReceiptEditorExport
    {
        private static NeutralVisualReceiptCollector active;
        private static string profile;
        public static int RealEventCount => active == null ? 0 :
            active.ObservedCount;

        public static void Begin(string actualGameProfileId)
        {
            active = new NeutralVisualReceiptCollector(actualGameProfileId);
            profile = actualGameProfileId;
        }

        public static void Observe(PresentationEvent actualEngineEvent)
        {
            if (active == null)
                throw new InvalidOperationException(
                    "Begin real Engine event session before Observe");
            active.Observe(actualEngineEvent);
        }

        [MenuItem("Tools/MATCH3 Engine/Export REAL M3P1 Events for VUF Review")]
        public static void SaveNewExternalReceipt()
        {
            try
            {
                if (active == null || active.ObservedCount == 0)
                    throw new InvalidOperationException(
                        "No actual Engine PresentationEvents received");
                string file = EditorUtility.SaveFilePanel(
                    "NEW real Engine M3P1 event receipt only",
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "engine_actual_events_M3P1", "txt");
                if (String.IsNullOrEmpty(file)) return;
                string destination = Path.GetFullPath(file);
                string checkout = Path.GetFullPath(Directory.GetCurrentDirectory())
                    .TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                if (!destination.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                    destination.StartsWith(checkout, StringComparison.OrdinalIgnoreCase) ||
                    File.Exists(destination))
                    throw new IOException("must choose NEW external .txt outside project");
                for (var directory = new DirectoryInfo(
                    Path.GetDirectoryName(destination)); directory != null;
                    directory = directory.Parent)
                    if (!directory.Exists ||
                        (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("symlink/junction directory rejected");
                byte[] data = active.Export();
                using (var output = new FileStream(destination,
                    FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    output.Write(data, 0, data.Length);
                Debug.Log("M3P1 actual Engine typed event receipt for " +
                    profile + ": " + active.ObservedCount +
                    " events, read-only VUF review, NOT settled replay/owner WIN.");
            }
            catch (Exception ex)
            {
                Debug.LogError("M3P1 export refused: " + ex.Message);
            }
        }
    }
}
