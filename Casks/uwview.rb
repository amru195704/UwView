cask "uwview" do
  arch arm: "arm64", intel: "x64"

  version "1.9.1"
  sha256 arm:   "d1d13abf36cb804635c91c1dc59ed07e57e2131ade24c9d91673e0967bcef747",
         intel: "86ce6ca2d6e05d754a5770b65dd6694b64c52874c25d0d9037cedf3b7cbf1604"

  url "https://github.com/amru195704/UwView/releases/download/v#{version}/UwView-#{version}-mac-#{arch}.dmg"
  name "UwView"
  desc "Open and search huge text/log files, with the uvf command"
  homepage "https://github.com/amru195704/UwView"

  livecheck do
    url :url
    strategy :github_latest
  end

  app "UwView.app"
  binary "#{appdir}/UwView.app/Contents/MacOS/uvf"
end
