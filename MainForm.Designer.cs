using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace LocalCloud
{
    partial class MainForm
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 所有控件均由代码动态创建（见 CreateControls），此处仅做基础初始化
            this.Name = "MainForm";
            this.Text = "私有云盘";
            this.ResumeLayout(false);

            // 窗体加载事件（对应 VB 的 Handles MyBase.Load）
            this.Load += new System.EventHandler(this.MainForm_Load);
        }

        #endregion
    }
}
