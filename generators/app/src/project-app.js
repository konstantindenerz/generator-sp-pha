'use strict';
var chalk = require('chalk'),
  yosay = require('yosay'),
  path = require('path'),
  vsname = require('vs_projectname'),
  uuid = require('uuid');


module.exports = function(Generator) {
    Generator.prototype.copyApp = function(){
      var self = this;
      var directory = path.join('src',this.settings.solutionName + this.settings.solutionAppSuffix);
      this.sourceRoot(path.join(__dirname, '../templates/App'));
      function cpt(source, target){
        self.fs.copyTpl(self.templatePath(source),
        self.destinationPath(path.join(directory, target ? target : source )), self.settings);
      }
      function cp(source, target){
        self.copy(path.join(self.sourceRoot(),source),
        path.join(directory, target ? target : source ));
      }
      cpt('Solution.sln', this.settings.solutionName + '.sln');
      cpt('App.csproj', this.settings.solutionName +  this.settings.solutionAppSuffix + '.csproj');
      cpt('AppManifest.xml');
      cp('AppIcon.png')
    };
};
