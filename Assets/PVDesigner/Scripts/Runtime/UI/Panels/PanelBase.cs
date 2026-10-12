using PVDesigner.App;
using UnityEngine;

namespace PVDesigner.UI.Panels
{
    /// <summary>A tab of the side panel. Panels are rebuilt on structural changes and refreshed otherwise.</summary>
    public abstract class PanelBase
    {
        protected PVDesignerApp App;
        protected MainWindow Window;
        protected Form Form;
        public RectTransform Content { get; private set; }
        public RectTransform Host { get; set; }
        public abstract string Title { get; }

        private bool _needsRebuild = true;

        public void Attach(PVDesignerApp app, MainWindow window, RectTransform content)
        {
            App = app;
            Window = window;
            Content = content;
            Form = new Form(content);
        }

        protected bool Visible => Host != null && Host.gameObject.activeSelf;

        public void Rebuild()
        {
            Form.Clear();
            if (App.Project == null) return;
            Build();
            AfterBuild();
            _needsRebuild = false;
        }

        /// <summary>Called after each rebuild (used to remember the structure the form was built for).</summary>
        protected virtual void AfterBuild() { }

        public void RequestRebuild()
        {
            _needsRebuild = true;
            if (Visible) Rebuild();
        }

        public virtual void OnShow()
        {
            if (_needsRebuild) Rebuild();
            else Form.Refresh();
        }

        protected abstract void Build();

        /// <summary>Default: rebuild when a whole project is loaded, otherwise refresh bound values.</summary>
        public virtual void OnChanged(ChangeKind kind)
        {
            if (kind == ChangeKind.ProjectLoaded) RequestRebuild();
            else if (Visible) Form.Refresh();
        }

        public virtual void OnResults()
        {
            if (Visible) Form.Refresh();
        }
    }
}
