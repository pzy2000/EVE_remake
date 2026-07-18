#!/usr/bin/env ruby
# frozen_string_literal: true

require "psych"

path = ARGV.fetch(0) do
  warn "Usage: #{File.basename($PROGRAM_NAME)} WORKFLOW_YAML"
  exit 2
end

document = Psych.parse_file(path)
abort("#{path}: YAML document is empty") unless document

walk = nil
walk = lambda do |node|
  if node.is_a?(Psych::Nodes::Mapping)
    seen = {}
    node.children.each_slice(2) do |key, value|
      if key.is_a?(Psych::Nodes::Scalar)
        if seen.key?(key.value)
          first_line = seen.fetch(key.value)
          abort(
            "#{path}:#{key.start_line + 1}: duplicate key #{key.value.inspect} " \
            "(first declared on line #{first_line})"
          )
        end
        seen[key.value] = key.start_line + 1
      end
      walk.call(key)
      walk.call(value)
    end
  else
    Array(node.children).each { |child| walk.call(child) } if node.respond_to?(:children)
  end
end

walk.call(document)

source = File.read(path, encoding: "UTF-8")
if File.basename(path) == "android.yml"
  job = lambda do |name|
    match = source.match(/^  #{Regexp.escape(name)}:\n(?<body>.*?)(?=^  [a-z0-9-]+:\n|\z)/m)
    abort("#{path}: missing #{name} job") unless match

    match[:body]
  end

  android_release = job.call("android-release")
  abort("#{path}: android-release must run for every successful push") unless
    android_release.include?("github.event_name == 'push'")
  abort("#{path}: android-release must not be restricted to main") if
    android_release.include?("github.ref == 'refs/heads/main'")
  abort("#{path}: android-release must expose version_name") unless
    android_release.include?("version_name: ${{ steps.version.outputs.version_name }}")

  github_release = job.call("github-release")
  required_release_fragments = [
    "needs: android-release",
    "contents: write",
    "gh release create",
    "--prerelease",
    "${{ needs.android-release.outputs.artifact_name }}",
    "${{ needs.android-release.outputs.version_code }}",
    "${{ needs.android-release.outputs.version_name }}",
    "*.apk",
    "SHA256SUMS"
  ]
  required_release_fragments.each do |fragment|
    abort("#{path}: github-release is missing #{fragment.inspect}") unless
      github_release.include?(fragment)
  end

  play_release = job.call("play-internal")
  abort("#{path}: Play publication must remain explicitly disabled until approval") unless
    play_release.include?("vars.ENABLE_PLAY_INTERNAL == 'true'")
end
