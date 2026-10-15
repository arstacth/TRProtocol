using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TRProtocol
{
	public class MainForm : Form
	{
		private const int WmNclButtonDown = 161;
		private const int HtCaption = 2;

		private static readonly Color Bg = Color.FromArgb(22, 22, 24);
		private static readonly Color Panel = Color.FromArgb(32, 32, 36);
		private static readonly Color Border = Color.FromArgb(58, 58, 64);
		private static readonly Color Fg = Color.FromArgb(232, 232, 236);
		private static readonly Color Muted = Color.FromArgb(150, 150, 158);
		private static readonly Color Accent = Color.FromArgb(88, 166, 255);
		private static readonly Color AccentHover = Color.FromArgb(110, 180, 255);
		private static readonly Color Ok = Color.FromArgb(110, 200, 140);
		private static readonly Color Err = Color.FromArgb(232, 110, 110);

		private Label titleLabel;
		private Label subtitleLabel;
		private Button closeButton;
		private Label pathCaptionLabel;
		private TextBox pathBox;
		private Button browseButton;
		private Button dumpButton;
		private Label statusLabel;
		private TextBox outputBox;

		protected override CreateParams CreateParams
		{
			get
			{
				CreateParams createParams = base.CreateParams;
				createParams.ClassStyle |= 0x20000;
				return createParams;
			}
		}

		public MainForm()
		{
			InitializeComponent();
			DoubleBuffered = true;
			SetStatus("Drop trgame.exe or trgame_unpacked.exe, then Dump.", Muted);
			titleLabel.MouseDown += OnCaptionMouseDown;
			subtitleLabel.MouseDown += OnCaptionMouseDown;
			pathCaptionLabel.MouseDown += OnCaptionMouseDown;
			statusLabel.MouseDown += OnCaptionMouseDown;
			MouseDown += OnCaptionMouseDown;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			using (Pen pen = new Pen(Border))
			{
				e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
			}
		}

		private void closeButton_Click(object sender, EventArgs e)
		{
			Close();
		}

		private void browseButton_Click(object sender, EventArgs e)
		{
			using (OpenFileDialog dialog = new OpenFileDialog())
			{
				dialog.Title = "Select trgame.exe";
				dialog.Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*";
				dialog.FileName = Path.GetFileName(pathBox.Text);
				string directory = Path.GetDirectoryName(pathBox.Text);
				if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
				{
					dialog.InitialDirectory = directory;
				}
				if (dialog.ShowDialog(this) == DialogResult.OK)
				{
					pathBox.Text = dialog.FileName;
					DumpCurrent();
				}
			}
		}

		private void dumpButton_Click(object sender, EventArgs e)
		{
			DumpCurrent();
		}

		private void MainForm_DragEnter(object sender, DragEventArgs e)
		{
			e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)
				? DragDropEffects.Copy
				: DragDropEffects.None;
		}

		private void MainForm_DragDrop(object sender, DragEventArgs e)
		{
			if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length != 0)
			{
				pathBox.Text = files[0];
				DumpCurrent();
			}
		}

		private void DumpCurrent()
		{
			string path = (pathBox.Text ?? "").Trim().Trim('"');
			if (path.Length == 0 || !File.Exists(path))
			{
				SetStatus("Choose trgame.exe or trgame_unpacked.exe.", Err);
				return;
			}
			path = Path.GetFullPath(path);
			pathBox.Text = path;
			dumpButton.Enabled = false;
			browseButton.Enabled = false;
			SetStatus("Dumping...", Muted);
			Task.Run(() => OpcodeDump.DumpFile(path)).ContinueWith(task =>
			{
				BeginInvoke(new Action(() =>
				{
					dumpButton.Enabled = true;
					browseButton.Enabled = true;
					if (task.IsFaulted)
					{
						outputBox.Clear();
						Exception ex = task.Exception.GetBaseException();
						SetStatus(ex.Message, Err);
						return;
					}
					DumpResult result = task.Result;
					outputBox.Text = result.CsvText;
					outputBox.SelectionStart = 0;
					outputBox.ScrollToCaret();
					SetStatus(result.Status, Ok);
				}));
			});
		}

		private void SetStatus(string text, Color color)
		{
			statusLabel.Text = text;
			statusLabel.ForeColor = color;
		}

		private void OnCaptionMouseDown(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				ReleaseCapture();
				SendMessage(Handle, WmNclButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
			}
		}

		[DllImport("user32.dll")]
		private static extern bool ReleaseCapture();

		[DllImport("user32.dll")]
		private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

		private void InitializeComponent()
		{
			titleLabel = new Label();
			subtitleLabel = new Label();
			closeButton = new Button();
			pathCaptionLabel = new Label();
			pathBox = new TextBox();
			browseButton = new Button();
			dumpButton = new Button();
			statusLabel = new Label();
			outputBox = new TextBox();
			SuspendLayout();

			titleLabel.BackColor = Color.Transparent;
			titleLabel.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
			titleLabel.ForeColor = Fg;
			titleLabel.Location = new Point(14, 10);
			titleLabel.Size = new Size(400, 22);
			titleLabel.Text = "TRProtocol";

			subtitleLabel.BackColor = Color.Transparent;
			subtitleLabel.ForeColor = Muted;
			subtitleLabel.Location = new Point(14, 32);
			subtitleLabel.Size = new Size(460, 18);
			subtitleLabel.Text = "TalesRunner Protocol Dump";

			closeButton.BackColor = Panel;
			closeButton.Cursor = Cursors.Hand;
			closeButton.FlatAppearance.BorderSize = 0;
			closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(180, 60, 60);
			closeButton.FlatStyle = FlatStyle.Flat;
			closeButton.ForeColor = Fg;
			closeButton.Location = new Point(518, 8);
			closeButton.Size = new Size(28, 28);
			closeButton.Text = "×";
			closeButton.Click += closeButton_Click;

			pathCaptionLabel.BackColor = Color.Transparent;
			pathCaptionLabel.ForeColor = Muted;
			pathCaptionLabel.Location = new Point(14, 56);
			pathCaptionLabel.Size = new Size(400, 16);
			pathCaptionLabel.Text = "trgame.exe or trgame_unpacked.exe";

			pathBox.AllowDrop = true;
			pathBox.BackColor = Panel;
			pathBox.BorderStyle = BorderStyle.FixedSingle;
			pathBox.ForeColor = Fg;
			pathBox.Location = new Point(14, 74);
			pathBox.Size = new Size(420, 26);
			pathBox.DragDrop += MainForm_DragDrop;
			pathBox.DragEnter += MainForm_DragEnter;

			browseButton.BackColor = Panel;
			browseButton.Cursor = Cursors.Hand;
			browseButton.FlatAppearance.BorderSize = 0;
			browseButton.FlatStyle = FlatStyle.Flat;
			browseButton.ForeColor = Fg;
			browseButton.Location = new Point(440, 73);
			browseButton.Size = new Size(106, 28);
			browseButton.Text = "Browse";
			browseButton.Click += browseButton_Click;

			dumpButton.BackColor = Accent;
			dumpButton.Cursor = Cursors.Hand;
			dumpButton.FlatAppearance.BorderSize = 0;
			dumpButton.FlatAppearance.MouseOverBackColor = AccentHover;
			dumpButton.FlatStyle = FlatStyle.Flat;
			dumpButton.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
			dumpButton.ForeColor = Color.FromArgb(16, 16, 18);
			dumpButton.Location = new Point(14, 110);
			dumpButton.Size = new Size(532, 32);
			dumpButton.Text = "Dump";
			dumpButton.Click += dumpButton_Click;

			statusLabel.BackColor = Color.Transparent;
			statusLabel.ForeColor = Muted;
			statusLabel.Location = new Point(14, 150);
			statusLabel.Size = new Size(532, 32);
			statusLabel.Text = "CSV is saved next to the exe.";

			outputBox.AllowDrop = true;
			outputBox.BackColor = Panel;
			outputBox.BorderStyle = BorderStyle.FixedSingle;
			outputBox.Font = new Font("Consolas", 9f);
			outputBox.ForeColor = Fg;
			outputBox.Location = new Point(14, 186);
			outputBox.Multiline = true;
			outputBox.ReadOnly = true;
			outputBox.ScrollBars = ScrollBars.Both;
			outputBox.Size = new Size(532, 200);
			outputBox.WordWrap = false;
			outputBox.DragDrop += MainForm_DragDrop;
			outputBox.DragEnter += MainForm_DragEnter;

			AllowDrop = true;
			AutoScaleMode = AutoScaleMode.None;
			BackColor = Bg;
			ClientSize = new Size(560, 400);
			Controls.Add(outputBox);
			Controls.Add(statusLabel);
			Controls.Add(dumpButton);
			Controls.Add(browseButton);
			Controls.Add(pathBox);
			Controls.Add(pathCaptionLabel);
			Controls.Add(closeButton);
			Controls.Add(subtitleLabel);
			Controls.Add(titleLabel);
			Font = new Font("Segoe UI", 9f);
			ForeColor = Fg;
			FormBorderStyle = FormBorderStyle.None;
			StartPosition = FormStartPosition.CenterScreen;
			Text = "TRProtocol";
			DragDrop += MainForm_DragDrop;
			DragEnter += MainForm_DragEnter;
			ResumeLayout(false);
			PerformLayout();
		}
	}
}
