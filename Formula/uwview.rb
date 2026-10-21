class Uwview < Formula
  desc "巨大なログ・テキストを調べるビューア（GUI本体 + CLIの uvf を同梱）"
  homepage "https://uvp.y42u.net/"
  version "1.9.1"
  license :cannot_represent # PolyForm Internal Use License 1.0.0（OSI非準拠のため）

  on_macos do
    odie "macOS では `brew install --cask amru195704/uwview/uwview` を使ってください。"
  end

  if Hardware::CPU.arm?
    url "https://github.com/amru195704/UwView/releases/download/v#{version}/UwView-#{version}-linux-aarch64.tar.gz"
    sha256 "d378779916d2798ca23efe92823bed83ebcdba12e8de2ba1b4cb1b6f5c6913b7"
  else
    url "https://github.com/amru195704/UwView/releases/download/v#{version}/UwView-#{version}-linux-x86_64.tar.gz"
    sha256 "7e86ba9860b77ac350f2c07939bf8207bceae07de5f8b072ddd9c7bb11d33043"
  end

  def install
    bin.install "UwView.Desktop" => "UwView"
    bin.install "uvf"
  end

  test do
    system "#{bin}/uvf", "--version"
  end
end
