'use strict';
var generator = require('yeoman-generator');
var prompts = require('../prompts');
var ejs = require('ejs');
var utils = require('./utils');
var extend = require('extend');

module.exports = function(Generator) {
    Generator.prototype.askQuestions = function(){
      var done = this.async();
      var self = this;
      prompts = JSON.parse(ejs.render(JSON.stringify(prompts), this.settings));
      prompts.forEach(function(e){
        if(utils.validators[e.name]){
          e.validate = utils.validators[e.name];
        }
      });
      this.prompt(prompts, function(answers){
        extend(self.settings,answers);
        done();
      }.bind(this));
    };
};
