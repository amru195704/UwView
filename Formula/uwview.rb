class Uwview < Formula
  desc "巨大なログ・テキストを調べるビューア（GUI本体 + CLIの uvf を同梱）"
  homepage "https://uvp.y42u.net/"
  version "1.8.1.9"
  license :cannot_represent # PolyForm Internal Use License 1.0.0（OSI非準拠のため）

  on_macos do
    odie "macOS では `brew install --cask amru195704/uwview/uwview` を使ってください。"
  end

  if Hardware::CPU.arm?
    url "https://github.com/amru195704/UwView/releases/download/v#{version}/UwView-#{version}-linux-aarch64.tar.gz"
    sha256 "7bd08dc13342eb0a6d2d53fe0203a2669c0a47e82ecaa07ffeeddfe9b49bcfd3"
  else
    url "https://github.com/amru195704/UwView/releases/download/v#{version}/UwView-#{version}-linux-x86_64.tar.gz"
    sha256 "bc83bfb420b8d7155efdd48a8f3e4cf0d08307c5549c63b0c3350fa861d0cea2"
  end

  def install
    bin.install "UwView.Desktop" => "UwView"
    bin.install "uvf"
  end

  test do
    system "#{bin}/uvf", "--version"
  end
end
