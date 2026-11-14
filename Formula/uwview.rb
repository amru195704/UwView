class Uwview < Formula
  desc "巨大なログ・テキストを調べるビューア（GUI本体 + CLIの uvf を同梱）"
  homepage "https://uvp.y42u.net/"
  version "2.0.0"
  license :cannot_represent # PolyForm Internal Use License 1.0.0（OSI非準拠のため）

  on_macos do
    odie "macOS では `brew install --cask amru195704/uwview/uwview` を使ってください。"
  end

  if Hardware::CPU.arm?
    url "https://github.com/amru195704/UwView/releases/download/v#{version}/UwView-#{version}-linux-aarch64.tar.gz"
    sha256 "3148a28d8cb64ce7b8b1f2c9cc2acbfabb69a51a655b56a48d7b45774bdd92e8"
  else
    url "https://github.com/amru195704/UwView/releases/download/v#{version}/UwView-#{version}-linux-x86_64.tar.gz"
    sha256 "9a05392c052695fe22200caf43b5838cf496b1488713582d07e3237c8b943adf"
  end

  def install
    bin.install "UwView.Desktop" => "UwView"
    bin.install "uvf"
  end

  test do
    system "#{bin}/uvf", "--version"
  end
end
