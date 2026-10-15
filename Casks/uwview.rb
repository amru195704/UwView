cask "uwview" do
  arch arm: "arm64", intel: "x64"

  version "1.9.0"
  sha256 arm:   "ffe839aa6a9673e00e373cd4c2841851050edc559f70df17d227b87f13a10967",
         intel: "22d042884819aef62be472d7356e439698bdf4a8ab2814646582b3175ee8af04"

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
