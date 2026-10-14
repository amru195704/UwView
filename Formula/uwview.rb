class Uwview < Formula
  desc "巨大なログ・テキストを調べるビューア（GUI本体 + CLIの uvf を同梱）"
  homepage "https://uvp.y42u.net/"
  version "1.9.0"
  license :cannot_represent # PolyForm Internal Use License 1.0.0（OSI非準拠のため）

  on_macos do
    odie "macOS では `brew install --cask amru195704/uwview/uwview` を使ってください。"
  end

  if Hardware::CPU.arm?
    url "https://github.com/amru195704/UwView/releases/download/v#{version}/UwView-#{version}-linux-aarch64.tar.gz"
    sha256 "d6a936039e3196798ff5a7d3b6d87825c77a0e70684ec37487f38291225d7d17"
  else
    url "https://github.com/amru195704/UwView/releases/download/v#{version}/UwView-#{version}-linux-x86_64.tar.gz"
    sha256 "1e875fc303b5f785b28e997f0b95258159309df275cf6a03fa9e0f4384b91595"
  end

  def install
    bin.install "UwView.Desktop" => "UwView"
    bin.install "uvf"
  end

  test do
    system "#{bin}/uvf", "--version"
  end
end
