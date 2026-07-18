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
