class Uwview < Formula
  desc "巨大なログ・テキストを調べるビューア（GUI本体 + CLIの uvf を同梱）"
  homepage "https://uvp.y42u.net/"
  version "1.7.3.6.7"
  license :cannot_represent # PolyForm Internal Use License 1.0.0（OSI非準拠のため）

  on_macos do
    odie "macOS では `brew install --cask amru195704/uwview/uwview` を使ってください。"
  end

  if Hardware::CPU.arm?
    url "https://github.com/amru195704/UwView/releases/download/v#{version}/UwView-#{version}-linux-aarch64.tar.gz"
    sha256 "692f1e15e6c38379ec1b4d5ba88ef7114b9b7465cf194461569d0d26d1c5c768"
  else
    url "https://github.com/amru195704/UwView/releases/download/v#{version}/UwView-#{version}-linux-x86_64.tar.gz"
    sha256 "5cfe043fd8f52649ea8d1592f77835aba82cd066d509235d378698e7e62f7332"
  end

  def install
    bin.install "UwView.Desktop" => "UwView"
    bin.install "uvf"
  end

  test do
    system "#{bin}/uvf", "--version"
  end
end
